using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Cookies;

/// <summary>
///     This class implements RFC 7873 DNS Cookies, a lightweight defence against spoofed source
///     addresses. The server cookie is derived from a rotating secret, the client's source
///     address and its client cookie, so no per-client state is kept.
///     <see cref="Pipeline.DnsQueryProcessingPipeline" /> calls <see cref="Evaluate" /> for every
///     decoded query. It is always registered by <c>AddNinlilDnsServerCore</c> and switched by
///     <see cref="DnsCookieOptions.Enable" />. The secret lives in memory only, so every cookie
///     changes on restart and each cluster node issues its own.
/// </summary>
/// <param name="options">
///     The enable, require and rotation settings. Read through <see cref="IOptions{TOptions}" />,
///     so a change applies after a restart.
/// </param>
public sealed class DnsCookieService(IOptions<DnsCookieOptions> options) : IDisposable
{
    /// <summary>This constant contains the fixed client cookie length from RFC 7873 §4, in bytes.</summary>
    private const int ClientCookieLength = 8;

    /// <summary>
    ///     This constant contains the length of the server cookies this service generates, in
    ///     bytes. RFC 7873 §4 allows 8 to 32; 8 bytes of HMAC output is plenty against an off-path
    ///     attacker guessing it.
    /// </summary>
    private const int ServerCookieLength = 8;

    private readonly object _timerLock = new();

    private byte[] _currentSecret = RandomNumberGenerator.GetBytes(32);
    private byte[] _previousSecret;

    private Timer _timer;

    /// <summary>
    ///     This method checks a request against the cookie policy and returns what
    ///     <see cref="Pipeline.DnsQueryProcessingPipeline" /> should do with it. The first call
    ///     starts the secret rotation timer, so a test that never calls it runs no timer.
    /// </summary>
    /// <param name="request">The decoded request.</param>
    /// <param name="remoteAddress">The client's source address.</param>
    public DnsCookieOutcome Evaluate(DnsMessage request, IPAddress remoteAddress)
    {
        EnsureTimerStarted();

        if (!options.Value.Enable)
            return new DnsCookieOutcome(false, false, null);

        DnsEdnsOption cookieOption = request.Edns?.Options?.FirstOrDefault(option =>
            option.Code == (ushort)EdnsOptionCodeEnum.Cookie
        );

        DnsCookieVerificationResultEnum verification = Verify(cookieOption, remoteAddress);

        // RFC 7873 §5.2: when cookies are required, only a Valid server cookie is accepted. A
        // client-only cookie or an unrecognized server cookie gets BADCOOKIE with a fresh server
        // cookie, so a real client can retry. With no usable client cookie (absent or the wrong
        // length) there is nothing to echo, so the query is answered REFUSED.
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

        // Cookies are not required: a request with no cookie, or a malformed one, is answered
        // without a cookie option. Every other request, including an Invalid one, is answered
        // normally with a fresh server cookie attached.
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
    ///     This method checks a Cookie option against the current and previous secrets.
    /// </summary>
    /// <param name="cookieOption">The request's Cookie option, or <see langword="null" />.</param>
    /// <param name="remoteAddress">The client's source address.</param>
    public DnsCookieVerificationResultEnum Verify(
        DnsEdnsOption cookieOption,
        IPAddress remoteAddress
    )
    {
        if (cookieOption is null)
            return DnsCookieVerificationResultEnum.NotPresent;

        byte[] data = cookieOption.Data;

        // RFC 7873 §4: a Cookie option is the 8-byte client cookie alone, or followed by an
        // 8 to 32 byte server cookie. Any other length is malformed.
        if (
            data.Length != ClientCookieLength
            && (data.Length < ClientCookieLength + 8 || data.Length > ClientCookieLength + 32)
        )
            return DnsCookieVerificationResultEnum.Malformed;

        if (data.Length == ClientCookieLength)
            return DnsCookieVerificationResultEnum.ClientOnly;

        byte[] clientCookie = data[..ClientCookieLength];
        byte[] serverCookie = data[ClientCookieLength..];

        // A match shows this server issued the cookie to this address, so the source address
        // was not spoofed.
        if (
            ComputeServerCookie(clientCookie, remoteAddress, Volatile.Read(ref _currentSecret))
                .AsSpan()
                .SequenceEqual(serverCookie)
        )
            return DnsCookieVerificationResultEnum.Valid;

        // The cookie may predate the last rotation, so try the previous secret too.
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
    ///     This method builds the Cookie option for a response: the client's 8-byte cookie
    ///     unchanged, followed by a server cookie derived with the current secret.
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
    ///     This method replaces the current secret with a new random one and keeps the old one as
    ///     the previous secret, so cookies issued just before the rotation still validate.
    /// </summary>
    public void RotateSecret()
    {
        byte[] rotated = RandomNumberGenerator.GetBytes(32);
        Volatile.Write(ref _previousSecret, Volatile.Read(ref _currentSecret));
        Volatile.Write(ref _currentSecret, rotated);
    }

    /// <summary>
    ///     This method starts the secret rotation timer on first use. The interval is read once,
    ///     here.
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
    ///     This method derives a server cookie: HMAC-SHA256 over the client cookie and source
    ///     address, keyed by <paramref name="secret" /> and truncated to
    ///     <see cref="ServerCookieLength" /> bytes. The address is the normalized client address,
    ///     so an IPv4 client hashes as 4 bytes whether or not the socket is dual-stack. RFC 7873 only asks for a pseudorandom
    ///     function of the client and a server secret; this is not the RFC 9018 interoperable
    ///     format, so cookies are not shared across servers.
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
