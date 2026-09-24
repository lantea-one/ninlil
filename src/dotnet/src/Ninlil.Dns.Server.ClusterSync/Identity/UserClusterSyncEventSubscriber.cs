using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Store;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This class is the sending-side subscriber for <see cref="UserAccountChangedEvent" />,
///     forwarding a local user account's own identity/RBAC snapshot to every cluster peer over the
///     existing cluster channel. Deliberately <em>not</em> built on
///     <see cref="Shared.ClusterConfigSyncSubscriberBase{TEntity}" /> -- that shared base's own
///     <c>Serialize(TEntity)</c> hook is synchronous, but building a full
///     <see cref="UserSyncPayload" /> needs two genuinely asynchronous lookups (a user's own role
///     memberships and directly-granted permission claims) the shared shape has no way to thread
///     through; see <see cref="UserClusterConfigSyncApplier" />'s own doc comment for the parallel
///     reasoning on the receiving side.
///     <para>
///         <b>The decrypt-at-sender half of Phase 9-6d's decrypt/re-encrypt pipeline</b>: this
///         class decrypts <see cref="UserEntity.TwoFactorTotpSecretEncrypted" />/
///         <see cref="UserEntity.TwoFactorRecoveryCodesEncrypted" /> with <em>this node's own</em>
///         protector (the only protector that could ever have produced those bytes) before
///         building the outbound payload -- see <see cref="UserSyncPayload" />'s own doc comment
///         for why the payload itself always carries the plain-text value, relying on the cluster
///         channel's existing TLS layer for confidentiality in transit, rather than attempting to
///         re-encrypt for a receiving node whose own key ring this node has no access to at all.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped repository per event.</param>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">This node's own cluster configuration, read for its own node id.</param>
/// <param name="dataProtectionProvider">This node's own Data Protection key ring.</param>
/// <param name="logger">The logger a broadcast failure is recorded to.</param>
public class UserClusterSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<UserClusterSyncEventSubscriber> logger
) : IDnsServerEventSubscriber<UserAccountChangedEvent>
{
    /// <summary>
    ///     This node's own protector for a TOTP authenticator key -- see this class's own doc
    ///     comment for why decryption always happens with this node's own protector.
    /// </summary>
    private readonly IDataProtector _authenticatorKeyProtector =
        dataProtectionProvider.CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose);

    /// <summary>
    ///     This node's own protector for a two-factor recovery code set.
    /// </summary>
    private readonly IDataProtector _recoveryCodesProtector =
        dataProtectionProvider.CreateProtector(NinlilDnsUserStore.RecoveryCodesProtectorPurpose);

    /// <inheritdoc />
    public ValueTask HandleAsync(
        UserAccountChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        _ = BroadcastChangeAsync(
            domainEvent.UserId,
            domainEvent.IsDeleted,
            domainEvent.OccurredAtUtc,
            stoppingToken
        );
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method does the actual work of building and broadcasting one user-account
    ///     envelope, with every exception (other than cancellation) caught and logged so a
    ///     broadcast failure never propagates back into <c>DnsServerEventDispatcher</c> -- the
    ///     same "fire a detached background task, log rather than throw" shape every other
    ///     cluster-sync subscriber in this project already establishes.
    /// </summary>
    private async Task BroadcastChangeAsync(
        Guid userId,
        bool isDeleted,
        DateTimeOffset occurredAtUtc,
        CancellationToken stoppingToken
    )
    {
        try
        {
            Guid ownNodeId = clusterOptions.CurrentValue.NodeId;

            if (isDeleted)
            {
                await broadcaster.BroadcastAsync(
                    new ClusterConfigSyncEnvelope(
                        UserClusterConfigSyncApplier.EntityTypeName,
                        userId.ToString(),
                        IsDeleted: true,
                        PayloadJson: null,
                        occurredAtUtc,
                        ownNodeId
                    ),
                    stoppingToken
                );
                return;
            }

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IUserRepository userRepository =
                scope.ServiceProvider.GetRequiredService<IUserRepository>();

            UserEntity user = await userRepository.GetByIdAsync(userId, stoppingToken);

            // A benign race: the account was already deleted again by the time this detached
            // task actually ran. Nothing to broadcast.
            if (user is null)
                return;

            IReadOnlyList<RoleEntity> roles = await userRepository.ListRolesAsync(
                userId,
                stoppingToken
            );
            IReadOnlyList<UserClaimEntity> claims = await userRepository.ListClaimsAsync(
                userId,
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

            await broadcaster.BroadcastAsync(
                new ClusterConfigSyncEnvelope(
                    UserClusterConfigSyncApplier.EntityTypeName,
                    userId.ToString(),
                    IsDeleted: false,
                    JsonSerializer.Serialize(payload),
                    user.GetLastModifiedUtc(),
                    user.OriginatingNodeId ?? ownNodeId
                ),
                stoppingToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while broadcasting a user-account config-sync change for "
                    + "'{UserId}'.",
                userId
            );
        }
    }
}
