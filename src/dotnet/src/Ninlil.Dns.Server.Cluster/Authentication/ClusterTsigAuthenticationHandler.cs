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
///     This class authenticates one cluster-channel HTTP request against a pre-shared TSIG key,
///     the peer-to-peer analogue of <c>ApiTokenAuthenticationHandler</c>'s own machine-to-machine
///     scheme -- see that class's own doc comment for the shape this mirrors. It never produces a
///     human-operator identity: a successfully authenticated request's own
///     <see cref="ClaimsIdentity.Name" /> is the TSIG key name that signed it, nothing more --
///     correlating that key name back to a specific known peer (via
///     <c>ClusterNodeEntity.TsigKeyName</c>) is left to whichever cluster-channel controller
///     action actually needs to know which peer called it, not baked into the claims here, since
///     a single TSIG key can validly authenticate more than one peer relationship (an operator is
///     free to reuse a key across joins) and this handler has no reason to assume otherwise.
/// </summary>
/// <param name="options">This scheme's own configured options.</param>
/// <param name="logger">The logger factory used to create this handler's diagnostic logger.</param>
/// <param name="encoder">The URL encoder ASP.NET Core's base handler class requires.</param>
/// <param name="tsigKeyRepository">The repository used to look up the claimed signing key.</param>
/// <param name="secretProtector">The protector used to decrypt the claimed key's own shared secret.</param>
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
        // What/why: a cluster-channel request carries its whole proof of identity in one
        // dedicated header -- unlike a JWT or API-token request, there is no separate
        // Authorization scheme to fall back on, so a request missing it entirely is rejected
        // immediately, before any parsing or lookup work is attempted.
        if (
            !Request.Headers.TryGetValue(
                ClusterTsigAuthenticationDefaults.SignatureHeaderName,
                out StringValues headerValues
            )
        )
            return AuthenticateResult.Fail("The cluster channel signature header is missing.");

        // How: the header's single string value packs three fields (key name, Unix timestamp,
        // base64 MAC) into one colon-delimited value -- see TryParseHeaderValue's own doc
        // comment for the exact split-into-three-parts mechanism this call performs.
        if (
            !ClusterTsigRequestSigner.TryParseHeaderValue(
                headerValues.ToString(),
                out string tsigKeyName,
                out long unixTimestampSeconds,
                out byte[] claimedMac
            )
        )
            return AuthenticateResult.Fail("The cluster channel signature header is malformed.");

        // Why: a captured, previously valid request must not be replayable indefinitely, so its
        // own claimed timestamp is checked against this node's own clock before its signature is
        // even verified. How: the absolute difference between "now" and the claimed signing
        // time is compared against ClusterTsigRequestSigner's own fixed tolerance window.
        long nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (
            Math.Abs(nowUnixSeconds - unixTimestampSeconds)
            > ClusterTsigRequestSigner.ClockSkewToleranceSeconds
        )
            return AuthenticateResult.Fail(
                "The cluster channel request's own signature has expired or is not yet valid."
            );

        // What/why: the claimed key must actually exist locally, and must not have been disabled
        // since it was issued -- an operator disabling a key (a suspected leak, a decommissioned
        // peer) must take effect on the very next request, not merely block future invites/joins.
        TsigKeyEntity key = await tsigKeyRepository.FindByNameAsync(tsigKeyName);
        if (key is null or { IsDisabled: true })
            return AuthenticateResult.Fail(
                "The cluster channel signing key is unknown or disabled."
            );

        // Buffer the request body so it can be read here for MAC verification and still be read
        // again afterward by MVC's own model binder -- EnableBuffering rewinds the stream back to
        // position 0 once this handler is done with it.
        Request.EnableBuffering();
        byte[] bodyBytes;
        using (MemoryStream buffer = new())
        {
            await Request.Body.CopyToAsync(buffer);
            bodyBytes = buffer.ToArray();
        }
        Request.Body.Position = 0;

        // How: this node independently recomputes the exact same HMAC-SHA256 over the request's
        // own method/path/timestamp/body that the sender itself must have signed with, using the
        // claimed key's own decrypted secret. Why fixed-time compare: an ordinary byte-by-byte
        // "==" comparison would return as soon as the first mismatching byte is found, letting an
        // attacker infer correct prefix bytes one at a time from response timing; ComputeMac's
        // recomputed value is instead compared with CryptographicOperations.FixedTimeEquals,
        // which always inspects every byte regardless of where a mismatch occurs.
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

        // What/why: per this class's own doc comment, a successfully authenticated request's
        // identity is the signing key's own name, nothing more -- correlating that name back to
        // a specific known peer is deliberately left to whichever action actually needs it.
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
