using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Authentication;

/// <summary>
///     This class authenticates a cluster-channel HTTP request against a pre-shared TSIG key,
///     the peer counterpart of <c>ApiTokenAuthenticationHandler</c>. It checks the header
///     format, the timestamp window, that the named key exists and is enabled, and the HMAC
///     (see <see cref="ClusterTsigRequestSigner" />). The resulting identity is only the key
///     name, never an operator. Any enabled TSIG key is accepted, including keys created for
///     zone transfers or dynamic updates; the handler does not check that the key belongs to a
///     <c>ClusterNodeEntity</c>, and matching a key to a peer is left to the actions.
/// </summary>
/// <param name="options">The scheme options.</param>
/// <param name="logger">The logger factory used to create this handler's diagnostic logger.</param>
/// <param name="encoder">The URL encoder ASP.NET Core's base handler class requires.</param>
/// <param name="tsigKeyRepository">The repository used to look up the claimed signing key.</param>
/// <param name="secretProtector">The protector used to decrypt the claimed key's secret.</param>
public class ClusterTsigAuthenticationHandler(
    IOptionsMonitor<ClusterTsigAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITsigKeyRepository tsigKeyRepository,
    TsigSecretProtector secretProtector
) : AuthenticationHandler<ClusterTsigAuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // The signature header is the only credential, so a request without it fails at once.
        if (
            !Request.Headers.TryGetValue(
                ClusterTsigAuthenticationDefaults.SignatureHeaderName,
                out StringValues headerValues
            )
        )
            return AuthenticateResult.Fail("The cluster channel signature header is missing.");

        // Key name, Unix timestamp and base64 MAC, separated by colons.
        if (
            !ClusterTsigRequestSigner.TryParseHeaderValue(
                headerValues.ToString(),
                out string tsigKeyName,
                out long unixTimestampSeconds,
                out byte[] claimedMac
            )
        )
            return AuthenticateResult.Fail("The cluster channel signature header is malformed.");

        // The timestamp must be within ClockSkewToleranceSeconds of this node's clock, which
        // limits how long a captured request can be replayed. It is checked before the MAC.
        long nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (
            Math.Abs(nowUnixSeconds - unixTimestampSeconds)
            > ClusterTsigRequestSigner.ClockSkewToleranceSeconds
        )
            return AuthenticateResult.Fail(
                "The cluster channel request's own signature has expired or is not yet valid."
            );

        // The key must exist and be enabled; disabling a key takes effect on the next request.
        TsigKeyEntity key = await tsigKeyRepository.FindByNameAsync(tsigKeyName);
        if (key is null or { IsDisabled: true })
            return AuthenticateResult.Fail(
                "The cluster channel signing key is unknown or disabled."
            );

        // Buffer the body so it can be read here for the MAC and again by model binding; the
        // position is reset to 0 afterwards.
        Request.EnableBuffering();
        byte[] bodyBytes;
        using (MemoryStream buffer = new())
        {
            await Request.Body.CopyToAsync(buffer);
            bodyBytes = buffer.ToArray();
        }
        Request.Body.Position = 0;

        // Recompute the MAC with the key's secret and compare in constant time, so response
        // timing does not reveal how many leading bytes of a forged MAC were right. The key's
        // algorithm is not checked; HMAC-SHA256 is always used.
        byte[] secret = secretProtector.Unprotect(key.SecretEncrypted);
        byte[] expectedMac = ClusterTsigRequestSigner.ComputeMac(
            secret,
            Request.Method,
            Request.Path.Value ?? "",
            unixTimestampSeconds,
            bodyBytes
        );

        if (!CryptographicOperations.FixedTimeEquals(expectedMac, claimedMac))
            return AuthenticateResult.Fail(
                "The cluster channel request's own signature does not match."
            );

        // The identity is the key name only (see the class summary).
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, key.Name)],
            Scheme.Name,
            ClaimTypes.Name,
            null
        );
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
