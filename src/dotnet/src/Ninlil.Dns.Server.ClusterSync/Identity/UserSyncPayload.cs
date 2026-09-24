using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This record is the wire shape a <c>"User"</c> config-sync envelope's own
///     <c>ClusterConfigSyncEnvelope.PayloadJson</c> carries -- built and serialized by
///     <c>UserClusterSyncEventSubscriber</c> on the sending side, and deserialized by
///     <c>UserClusterConfigSyncApplier</c> on the receiving side.
///     <para>
///         <b>The decrypt-at-sender/re-encrypt-at-receiver pipeline (Phase 9-6d's own decision
///         record, decision #5)</b>: <see cref="TwoFactorTotpSecretPlain" />/
///         <see cref="TwoFactorRecoveryCodesPlain" /> are deliberately carried in <em>plain text</em>
///         here, not as the sending node's own <c>IDataProtector</c>-encrypted bytes -- a receiving
///         node's own, fully independent Data Protection key ring (per this project's own
///         "per-node key rings stay independent, never shared" invariant) could never decrypt a
///         value protected under a different node's own key ring anyway. The sending side
///         (<c>UserClusterSyncEventSubscriber</c>) decrypts with its own protector immediately
///         before building this payload; the receiving side
///         (<c>UserClusterConfigSyncApplier</c>) re-encrypts with <em>its own</em> protector
///         immediately after deserializing it, before the plain-text value is ever persisted. This
///         is exactly why the cluster channel needs the TLS layer Phase 9-6a already built on top
///         of TSIG: TSIG only authenticates the envelope's own integrity/origin, it does not
///         encrypt the payload in transit, so a real secret must never cross this wire without the
///         TLS confidentiality the channel already provides.
///     </para>
///     <para>
///         <see cref="PasswordHash" />/<see cref="SecurityStamp" />/<see cref="ConcurrencyStamp" />
///         are carried as-is, with no protector involved at all: unlike the two TOTP fields above,
///         none of these three is ever encrypted via <c>IDataProtector</c> in the first place --
///         <see cref="PasswordHash" /> is already a one-way, salted hash (ASP.NET Core Identity's
///         own <c>PasswordHasher</c>, portable by design across any two nodes running the same
///         hashing algorithm), and the other two are opaque random tokens, not secrets that need
///         decrypting to be usable.
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
