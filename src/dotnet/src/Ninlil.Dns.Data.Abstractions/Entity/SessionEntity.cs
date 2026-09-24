// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one issued refresh-token session for a <see cref="UserEntity" />.
///     The short-lived JWT access token itself is never stored anywhere -- by design, its
///     validity is checked purely by its signature and expiry -- but the longer-lived refresh
///     token it was paired with is tracked here so that a session can be looked up, listed to the
///     user it belongs to, and revoked on demand.
/// </summary>
public class SessionEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="UserEntity" /> this session
    ///     belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    ///     This property contains a salted hash of the refresh token. As with a password, the
    ///     plain-text refresh token is never stored, only ever returned once, at issuance.
    /// </summary>
    public string RefreshTokenHash { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time at which the refresh token expires and
    ///     the session can no longer be renewed.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time at which the session was revoked, or
    ///     <see langword="null" /> if it is still active.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }

    /// <summary>
    ///     This property contains the IP address the session was created from, kept for the
    ///     user's own security auditing (for example, "Sign-ins" list in the administration
    ///     console).
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    ///     This property contains the User-Agent header the session was created with.
    /// </summary>
    public string UserAgent { get; set; }

    /// <summary>
    ///     This property says whether this is a long-lived session -- a browser sign-in with
    ///     "Remember me" checked, or any API client holding its refresh token itself -- rather than
    ///     a browser-session sign-in that ends when the browser closes. It decides both the
    ///     session's lifetime (<c>JwtOptions.RefreshTokenLifetime</c> versus
    ///     <c>JwtOptions.BrowserSessionRefreshTokenLifetime</c>) and whether its refresh cookie
    ///     carries an expiry, and it is carried over unchanged every time the refresh token is
    ///     rotated, so a session never changes kind mid-way.
    /// </summary>
    public bool IsPersistent { get; set; }
}
