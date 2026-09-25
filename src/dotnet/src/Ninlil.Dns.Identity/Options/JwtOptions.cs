// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>Jwt</c> section of the application's configuration and
///     describes how Ninlil issues and validates JSON Web Tokens (RFC 7519). Tokens are signed
///     with HMAC-SHA256 (RFC 7518), which needs only one shared secret; that suits a server that
///     validates only the tokens it issues itself.
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
    ///     signature. It must be at least 32 bytes (256 bits) as UTF-8 and kept secret: anyone who
    ///     has it can forge tokens this server accepts.
    /// </summary>
    public string SigningKey { get; set; }

    /// <summary>
    ///     This property contains how long a new access token stays valid. It is short because a
    ///     leaked access token cannot be revoked before it expires; callers renew it with the
    ///     refresh token.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     This property contains how long a long-lived refresh token (a <c>SessionEntity</c>) stays
    ///     valid before the caller must sign in again.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how long a browser-session sign-in (one made without "Remember
    ///     me") stays renewable without activity. Each refresh rotates the token and restarts the
    ///     window, so an open admin console keeps its session, while an abandoned one (or one whose
    ///     cookie a browser restores after closing) expires on the server within this time,
    ///     whatever the browser does with the cookie.
    /// </summary>
    public TimeSpan BrowserSessionRefreshTokenLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    ///     This property contains how much clock drift is tolerated when checking a token's expiry.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}
