using Microsoft.AspNetCore.Http;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This class reads, writes, and clears the refresh-token cookie the browser admin console
///     signs in with -- shared by the local sign-in endpoints and the OpenID Connect sign-in
///     handler, so every path that issues a browser session sets exactly the same cookie.
///     <para>
///         The cookie is <c>HttpOnly</c> (page script can never read it), <c>SameSite=Strict</c>
///         (a cross-site page can never make the browser send it), and scoped to
///         <see cref="CookiePath" />, so it accompanies only the refresh and sign-out calls rather
///         than every API request. It is marked <c>Secure</c> whenever the request itself arrived
///         over HTTPS (as seen after trusted forwarded headers); a homelab console served over
///         plain HTTP still works, with the same exposure its access token already has there.
///     </para>
///     <para>
///         A persistent session's cookie expires with the session itself; a browser-session one
///         carries no expiry, so the browser discards it on close, and the server's own shorter
///         browser-session lifetime bounds it even if a browser restores it anyway.
///     </para>
/// </summary>
public static class RefreshTokenCookie
{
    /// <summary>The cookie's name.</summary>
    public const string CookieName = "ninlil_refresh";

    /// <summary>The path the cookie is scoped to: the authentication endpoints only.</summary>
    public const string CookiePath = "/api/auth";

    /// <summary>
    ///     This method sets the cookie to a newly issued refresh token.
    /// </summary>
    /// <param name="context">The request whose response carries the cookie.</param>
    /// <param name="rawRefreshToken">The raw refresh token.</param>
    /// <param name="session">The session the token belongs to, for its persistence and expiry.</param>
    public static void Write(HttpContext context, string rawRefreshToken, SessionEntity session) =>
        context.Response.Cookies.Append(
            CookieName,
            rawRefreshToken,
            BuildOptions(context, session.IsPersistent ? session.ExpiresAtUtc : null)
        );

    /// <summary>
    ///     This method reads the refresh token the browser presented, if any.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <returns>The raw refresh token, or <see langword="null" /> when no cookie was sent.</returns>
    public static string Read(HttpContext context) =>
        context.Request.Cookies.TryGetValue(CookieName, out string value)
        && !string.IsNullOrEmpty(value)
            ? value
            : null;

    /// <summary>
    ///     This method tells the browser to discard the cookie.
    /// </summary>
    /// <param name="context">The request whose response clears the cookie.</param>
    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, BuildOptions(context, null));

    /// <summary>
    ///     This method builds the cookie's attributes; <paramref name="expires" /> is
    ///     <see langword="null" /> for a browser-session cookie.
    /// </summary>
    private static CookieOptions BuildOptions(HttpContext context, DateTimeOffset? expires) =>
        new()
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = expires,
            IsEssential = true,
        };
}
