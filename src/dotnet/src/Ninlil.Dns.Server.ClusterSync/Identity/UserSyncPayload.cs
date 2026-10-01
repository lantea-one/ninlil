using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This record is the JSON payload of a <c>"User"</c> config-sync envelope, built by
///     <see cref="UserClusterSyncEventSubscriber" /> and applied by
///     <see cref="UserClusterConfigSyncApplier" />.
///     <para>
///         <b>Secrets in plaintext.</b> <see cref="TwoFactorTotpSecretPlain" /> and
///         <see cref="TwoFactorRecoveryCodesPlain" /> are sent decrypted, because each node has
///         its own Data Protection key ring and could not decrypt another node's ciphertext. The
///         sender decrypts them just before building the payload and the receiver encrypts them
///         with its own key ring before storing them. The HMAC signature does not encrypt
///         anything; the cluster channel's TLS is what keeps these values confidential in
///         transit.
///     </para>
///     <para>
///         <see cref="PasswordHash" />, <see cref="SecurityStamp" /> and <see cref="ConcurrencyStamp" />
///         are sent as they are: none is encrypted with <c>IDataProtector</c>. The password hash
///         is a salted one-way hash from ASP.NET Core Identity's <c>PasswordHasher</c>, valid on
///         any node, and the two stamps are random tokens.
///     </para>
/// </summary>
public sealed record UserSyncPayload(
    string UserName,
    string NormalizedUserName,
    string DisplayName,
    string Email,
    string NormalizedEmail,
    bool EmailConfirmed,
    string PasswordHash,
    string SecurityStamp,
    string ConcurrencyStamp,
    string PhoneNumber,
    bool PhoneNumberConfirmed,
    bool TwoFactorEnabled,
    string TwoFactorTotpSecretPlain,
    string TwoFactorRecoveryCodesPlain,
    DateTimeOffset? LockoutEndUtc,
    bool LockoutEnabled,
    int AccessFailedCount,
    bool IsDisabled,
    UiThemePaletteEnum? PreferredPalette,
    UiThemeModeEnum? PreferredMode,
    string ExternalIdentityProvider,
    string ExternalSubjectId,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> DirectPermissionClaims
);
