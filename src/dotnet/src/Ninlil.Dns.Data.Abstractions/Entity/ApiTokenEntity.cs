using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents an API token issued to a <see cref="UserEntity" /> for
///     machine-to-machine access -- as an alternative to signing in interactively and obtaining a
///     JWT. See <see cref="ApiTokenTypeEnum" /> for the two ways such a token can behave.
/// </summary>
public class ApiTokenEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="UserEntity" /> this token
    ///     authenticates as.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    ///     This property contains the human-readable name given to the token when it was created,
    ///     such as "Home Lab Dynamic DNS Updater".
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains a salted hash of the token's secret value. As with a password,
    ///     the plain-text token is only ever shown once, at the moment it is issued.
    /// </summary>
    public string TokenHash { get; set; }

    /// <summary>
    ///     This property contains whether the token is <see cref="ApiTokenTypeEnum.Static" /> or
    ///     <see cref="ApiTokenTypeEnum.OneTime" />.
    /// </summary>
    public ApiTokenTypeEnum Type { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time after which the token is no longer
    ///     accepted, or <see langword="null" /> if it does not expire on its own.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time at which a
    ///     <see cref="ApiTokenTypeEnum.OneTime" /> token was consumed, or <see langword="null" />
    ///     if it has not been used yet. This field is meaningless for a
    ///     <see cref="ApiTokenTypeEnum.Static" /> token, which can be reused until it expires or
    ///     is revoked.
    /// </summary>
    public DateTimeOffset? ConsumedAtUtc { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time at which the token was manually revoked,
    ///     or <see langword="null" /> if it has not been revoked.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
