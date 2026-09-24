// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>Jwt</c> section of the application's configuration and
///     describes how Ninlil issues and validates the JSON Web Tokens defined by RFC 7519. A
///     token's signature uses HMAC-SHA256 (RFC 7518), which needs nothing more than a single
///     shared secret to configure -- appropriate for a self-hosted server that issues and
///     validates its own tokens, rather than trusting tokens issued by a separate authority.
/// </summary>
public class JwtOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    ///     This property contains the value placed in a token's <c>iss</c> (issuer) claim, and
    ///     the value this server requires an incoming token's <c>iss</c> claim to match.
    /// </summary>
    public string Issuer { get; set; } = "Ninlil.Dns";

    /// <summary>
    ///     This property contains the value placed in a token's <c>aud</c> (audience) claim, and
    ///     the value this server requires an incoming token's <c>aud</c> claim to match.
    /// </summary>
    public string Audience { get; set; } = "Ninlil.Web";

    /// <summary>
    ///     This property contains the shared secret used to sign and validate a token's HMAC-SHA256
    ///     signature. It must be at least 32 bytes (256 bits) once UTF-8 encoded, and must be kept
    ///     confidential -- anyone who has it can forge a token this server will accept.
    /// </summary>
    public string SigningKey { get; set; }

    /// <summary>
    ///     This property contains how long a newly-issued access token remains valid. Kept short,
    ///     since a leaked access token cannot be revoked before it expires on its own; the caller
    ///     is expected to use the accompanying refresh token to obtain a new one instead of
    ///     requesting a long-lived access token up front.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     This property contains how long a newly-issued refresh token, tracked as a
    ///     <c>SessionEntity</c>, remains valid before the caller must sign in again from scratch.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how long a browser-session sign-in (one made without "Remember
    ///     me") stays renewable without activity. Every refresh rotates the token and restarts
    ///     this window, so an admin console left open keeps its session, while one abandoned (or
    ///     whose cookie a browser restores after closing) expires on the server within this long
    ///     regardless of what the browser does with the cookie.
    /// </summary>
    public TimeSpan BrowserSessionRefreshTokenLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    ///     This property contains how much clock drift, between this server and whichever server
    ///     eventually validates a token, is tolerated when checking a token's expiry.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}
