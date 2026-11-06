using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Store;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This class is the <see cref="IClusterConfigSyncSource" /> for user accounts: identity data,
///     role memberships and direct permissions, as <see cref="UserClusterSyncEventSubscriber" />
///     sends them for a change. It is stage <see cref="ClusterConfigBackfillStages.Dependents" />,
///     because the receiver skips a membership in a role it does not hold.
///     <para>
///         The two-factor secrets are stored encrypted with this node's key ring, which no other
///         node has, so the source decrypts <see cref="UserEntity.TwoFactorTotpSecretEncrypted" />
///         and <see cref="UserEntity.TwoFactorRecoveryCodesEncrypted" /> and sends them as
///         plaintext in <see cref="UserSyncPayload" />. That is acceptable only inside the
///         pinned-TLS, signed cluster channel; the receiving applier encrypts them with its own
///         key ring, and ciphertext never leaves the node. When this node cannot decrypt a secret
///         (the key ring changed), the backfill skips that account with a warning that names it,
///         because sending the account without its secret would silently turn two-factor
///         authentication off on the peer.
///     </para>
/// </summary>
public sealed class UserClusterConfigSyncSource : ClusterConfigSyncSourceBase<UserEntity>
{
    private readonly IUserRepository _repository;
    private readonly IDataProtector _authenticatorKeyProtector;
    private readonly IDataProtector _recoveryCodesProtector;

    /// <summary>
    ///     This constructor creates the protectors with the purposes <c>NinlilDnsUserStore</c> uses
    ///     to encrypt the secrets.
    /// </summary>
    /// <param name="repository">The user repository, read for the user, its roles and claims.</param>
    /// <param name="dataProtectionProvider">This node's Data Protection provider.</param>
    /// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
    /// <param name="logger">The logger a skipped row is reported to.</param>
    public UserClusterConfigSyncSource(
        IUserRepository repository,
        IDataProtectionProvider dataProtectionProvider,
        IOptionsMonitor<ClusterOptions> clusterOptions,
        ILogger<UserClusterConfigSyncSource> logger
    )
        : base(repository, clusterOptions, logger)
    {
        _repository = repository;
        _authenticatorKeyProtector = dataProtectionProvider.CreateProtector(
            NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose
        );
        _recoveryCodesProtector = dataProtectionProvider.CreateProtector(
            NinlilDnsUserStore.RecoveryCodesProtectorPurpose
        );
    }

    /// <inheritdoc />
    public override string EntityType => UserClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public override int BackfillStage => ClusterConfigBackfillStages.Dependents;

    /// <inheritdoc />
    protected override async ValueTask<string> SerializeAsync(
        UserEntity user,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<RoleEntity> roles = await _repository.ListRolesAsync(user.Id, stoppingToken);
        IReadOnlyList<UserClaimEntity> claims = await _repository.ListClaimsAsync(
            user.Id,
            stoppingToken
        );

        UserSyncPayload payload = new(
            user.UserName,
            user.NormalizedUserName,
            user.DisplayName,
            user.Email,
            user.NormalizedEmail,
            user.EmailConfirmed,
            user.PasswordHash,
            user.SecurityStamp,
            user.ConcurrencyStamp,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.TwoFactorEnabled,
            user.TwoFactorTotpSecretEncrypted is null
                ? null
                : _authenticatorKeyProtector.Unprotect(user.TwoFactorTotpSecretEncrypted),
            user.TwoFactorRecoveryCodesEncrypted is null
                ? null
                : _recoveryCodesProtector.Unprotect(user.TwoFactorRecoveryCodesEncrypted),
            user.LockoutEndUtc,
            user.LockoutEnabled,
            user.AccessFailedCount,
            user.IsDisabled,
            user.PreferredPalette,
            user.PreferredMode,
            user.ExternalIdentityProvider,
            user.ExternalSubjectId,
            [.. roles.Select(role => role.Id)],
            [
                .. claims
                    .Where(claim => claim.ClaimType == NinlilDnsClaimTypes.Permission)
                    .Select(claim => claim.ClaimValue),
            ]
        );

        return JsonSerializer.Serialize(payload);
    }
}
