// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one refresh-token session of a <see cref="UserEntity" />. The
///     short-lived JWT access token is never stored, since it is checked by signature and expiry
///     alone. The longer-lived refresh token paired with it is tracked here so a session can be
///     looked up, shown to its user and revoked.
/// </summary>
public class SessionEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="UserEntity" /> this session
    ///     belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    ///     This property contains the SHA-256 hash of the refresh token (see <c>TokenHasher</c>). The
    ///     plain-text token is never stored; it is returned once, when issued.
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
    ///     This property contains the IP address the session was created from, kept so users can
    ///     review their sign-ins in the admin console.
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    ///     This property contains the User-Agent header the session was created with.
    /// </summary>
    public string UserAgent { get; set; }

    /// <summary>
    ///     This property says whether this is a long-lived session (a browser sign-in with "Remember
    ///     me", or an API client holding its own refresh token) rather than a browser session that
    ///     ends when the browser closes. It decides the session lifetime
    ///     (<c>JwtOptions.RefreshTokenLifetime</c> or
    ///     <c>JwtOptions.BrowserSessionRefreshTokenLifetime</c>) and whether the refresh cookie has an
    ///     expiry. Refresh-token rotation copies it, so a session never changes kind.
    /// </summary>
    public bool IsPersistent { get; set; }
}
