using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Cookies;

/// <summary>
///     This class implements RFC 7873 DNS Cookies: a lightweight, stateless-to-the-server
///     anti-spoofing mechanism, generating and validating a server cookie derived from a
///     periodically rotated secret plus each client's own source address and client cookie --
///     no per-client state is ever stored, unlike <see cref="RateLimiting.IDnsRateLimiter" />'s
///     own token buckets. Registered unconditionally by <c>Server.Core</c>'s own DI extension
///     (this class lives in <c>Server.Core</c> itself, not a separate project, since it needs no
///     storage of its own -- the same reasoning already applied to
///     <see cref="Certificates.TlsCertificateLoader" />), gated at runtime by
///     <see cref="DnsCookieOptions.Enable" />.
/// </summary>
/// <param name="options">This service's own configured enablement, requirement, and rotation interval.</param>
public sealed class DnsCookieService(IOptions<DnsCookieOptions> options) : IDisposable
{
    /// <summary>This constant contains RFC 7873 §4's fixed client-cookie length, in bytes.</summary>
    private const int ClientCookieLength = 8;

    /// <summary>
    ///     This constant contains the server-cookie length this service itself generates, in
    ///     bytes. RFC 7873 §4 allows 8-32; 8 is the minimum, chosen since nothing about this
    ///     service's own derivation needs more entropy than that to be an effective anti-spoofing
    ///     signal.
    /// </summary>
    private const int ServerCookieLength = 8;

    private readonly object _timerLock = new();

    private byte[] _currentSecret = RandomNumberGenerator.GetBytes(32);
    private byte[] _previousSecret;

    private Timer _timer;

    /// <summary>
    ///     This method evaluates a single request against this server's own cookie policy,
    ///     returning what <see cref="Pipeline.DnsQueryProcessingPipeline" /> should do about it.
    ///     Starts this service's own secret-rotation timer on first use, rather than in the
    ///     constructor, so a test that never calls this method never pays for a live
    ///     <see cref="Timer" /> either.
    /// </summary>
    /// <param name="request">The decoded request.</param>
    /// <param name="remoteAddress">The querying client's own source address.</param>
    public DnsCookieOutcome Evaluate(DnsMessage request, IPAddress remoteAddress)
    {
        EnsureTimerStarted();

        if (!options.Value.Enable)
            return new DnsCookieOutcome(false, false, null);

        DnsEdnsOption cookieOption = request.Edns?.Options?.FirstOrDefault(option =>
            option.Code == (ushort)EdnsOptionCodeEnum.Cookie
        );

        DnsCookieVerificationResultEnum verification = Verify(cookieOption, remoteAddress);

        // RFC 7873 §5.2: when cookies are required, only a Valid server cookie is accepted
        // outright. A cookie this server can still work with -- the client sent only its own
        // 8-byte cookie, or sent a full cookie this server's own secret no longer recognizes --
        // is answered BADCOOKIE together with a freshly derived server cookie, so a legitimate
        // client can retry once with a cookie this server will actually accept. A cookie with no
        // recoverable client cookie at all (absent, or the wrong length to safely echo back) is
        // dropped instead: there is nothing valid to build a BADCOOKIE response's own cookie
        // option around.
        if (options.Value.Require)
        {
            switch (verification)
            {
                case DnsCookieVerificationResultEnum.Valid:
                    break;
                case DnsCookieVerificationResultEnum.ClientOnly:
                case DnsCookieVerificationResultEnum.Invalid:
                    return new DnsCookieOutcome(
                        false,
                        true,
                        BuildResponseOption(cookieOption.Data[..ClientCookieLength], remoteAddress)
                    );
                default: // NotPresent or Malformed: nothing recoverable to answer BADCOOKIE with.
                    return new DnsCookieOutcome(true, false, null);
            }
        }

        // Cookies are not required: a request with no cookie at all, or one too malformed to
        // safely echo a client cookie back from, is answered normally with no cookie option of
        // its own. Every other outcome -- including an Invalid one, since this server is not
        // enforcing cookies -- still gets a fresh server cookie attached to its response, so a
        // client that does support cookies can start using them without this server ever having
        // rejected a query outright.
        if (
            verification
            is DnsCookieVerificationResultEnum.NotPresent
                or DnsCookieVerificationResultEnum.Malformed
        )
            return new DnsCookieOutcome(false, false, null);

        return new DnsCookieOutcome(
            false,
            false,
            BuildResponseOption(cookieOption.Data[..ClientCookieLength], remoteAddress)
        );
    }

    /// <summary>
    ///     This method checks a single Cookie option against this server's own current and
    ///     immediately preceding secret.
    /// </summary>
    /// <param name="cookieOption">The request's own Cookie option, or <see langword="null" />.</param>
    /// <param name="remoteAddress">The querying client's own source address.</param>
    public DnsCookieVerificationResultEnum Verify(
        DnsEdnsOption cookieOption,
        IPAddress remoteAddress
    )
    {
        if (cookieOption is null)
            return DnsCookieVerificationResultEnum.NotPresent;

        byte[] data = cookieOption.Data;

        // RFC 7873 §4: a Cookie option is either exactly the 8-byte client cookie alone, or the
        // 8-byte client cookie followed by an 8-to-32-byte server cookie -- any other total
        // length is not a shape this server's own wire format ever produced or can meaningfully
        // interpret.
        if (
            data.Length != ClientCookieLength
            && (data.Length < ClientCookieLength + 8 || data.Length > ClientCookieLength + 32)
        )
            return DnsCookieVerificationResultEnum.Malformed;

        if (data.Length == ClientCookieLength)
            return DnsCookieVerificationResultEnum.ClientOnly;

        byte[] clientCookie = data[..ClientCookieLength];
        byte[] serverCookie = data[ClientCookieLength..];

        // Re-derive what a server cookie for this client cookie/address pair should look like
        // under the current secret, and compare. A match confirms this server (not a spoofed
        // source address) actually issued the client's cookie.
        if (
            ComputeServerCookie(clientCookie, remoteAddress, Volatile.Read(ref _currentSecret))
                .AsSpan()
                .SequenceEqual(serverCookie)
        )
            return DnsCookieVerificationResultEnum.Valid;

        // A mismatch against the current secret does not necessarily mean the cookie is invalid
        // -- it may simply predate the most recent rotation. Re-check against the immediately
        // preceding secret before giving up, so a cookie issued moments before a scheduled
        // rotation does not spuriously fail verification.
        byte[] previousSecret = Volatile.Read(ref _previousSecret);

        if (
            previousSecret is not null
            && ComputeServerCookie(clientCookie, remoteAddress, previousSecret)
                .AsSpan()
                .SequenceEqual(serverCookie)
        )
            return DnsCookieVerificationResultEnum.Valid;

        return DnsCookieVerificationResultEnum.Invalid;
    }

    /// <summary>
    ///     This method builds the Cookie option a response should carry: the client's own
    ///     8-byte cookie, echoed back unchanged, followed by a freshly derived server cookie.
    /// </summary>
    public DnsEdnsOption BuildResponseOption(byte[] clientCookie, IPAddress remoteAddress)
    {
        byte[] serverCookie = ComputeServerCookie(
            clientCookie,
            remoteAddress,
            Volatile.Read(ref _currentSecret)
        );

        return new DnsEdnsOption(
            (ushort)EdnsOptionCodeEnum.Cookie,
            [.. clientCookie, .. serverCookie]
        );
    }

    /// <summary>
    ///     This method replaces this service's own current secret with a freshly generated one,
    ///     keeping the immediately preceding secret around so a cookie issued just before this
    ///     rotation does not suddenly stop validating.
    /// </summary>
    public void RotateSecret()
    {
        byte[] rotated = RandomNumberGenerator.GetBytes(32);
        Volatile.Write(ref _previousSecret, Volatile.Read(ref _currentSecret));
        Volatile.Write(ref _currentSecret, rotated);
    }

    /// <summary>
    ///     This method starts this service's own secret-rotation timer the first time it is
    ///     actually needed.
    /// </summary>
    private void EnsureTimerStarted()
    {
        if (_timer is not null)
            return;

        lock (_timerLock)
        {
            if (_timer is not null)
                return;

            TimeSpan interval = options.Value.SecretRotationInterval;
            _timer = new Timer(_ => RotateSecret(), null, interval, interval);
        }
    }

    /// <summary>
    ///     This method derives a server cookie: an HMAC-SHA256 of the client's own source
    ///     address and client cookie, keyed by <paramref name="secret" /> and truncated to
    ///     <see cref="ServerCookieLength" /> bytes. RFC 7873 does not mandate a specific
    ///     derivation function, only that it be a pseudorandom function of the client's own
    ///     identity and a secret only this server knows.
    /// </summary>
    private static byte[] ComputeServerCookie(
        byte[] clientCookie,
        IPAddress remoteAddress,
        byte[] secret
    )
    {
        byte[] material = [.. clientCookie, .. remoteAddress.GetAddressBytes()];
        using HMACSHA256 hmac = new(secret);
        return hmac.ComputeHash(material)[..ServerCookieLength];
    }

    /// <inheritdoc />
    public void Dispose() => _timer?.Dispose();
}
