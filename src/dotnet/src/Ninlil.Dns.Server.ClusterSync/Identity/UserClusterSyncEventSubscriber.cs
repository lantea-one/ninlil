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
///     This class is the sending-side subscriber for <see cref="UserAccountChangedEvent" />. It
///     sends a user account's identity data, role memberships and direct permissions to every
///     healthy peer. It is not built on <see cref="Shared.ClusterConfigSyncSubscriberBase{TEntity}" />,
///     whose <c>Serialize</c> hook is synchronous, because the roles and claims need two
///     asynchronous lookups.
///     <para>
///         It decrypts <see cref="UserEntity.TwoFactorTotpSecretEncrypted" /> and
///         <see cref="UserEntity.TwoFactorRecoveryCodesEncrypted" /> with this node's protectors,
///         the only ones that can, and sends them in plaintext over the TLS channel (see
///         <see cref="UserSyncPayload" />).
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope, and a repository, per event.</param>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="dataProtectionProvider">This node's Data Protection provider.</param>
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
    ///     This field contains the protector for TOTP authenticator keys, with the purpose
    ///     <c>NinlilDnsUserStore</c> uses.
    /// </summary>
    private readonly IDataProtector _authenticatorKeyProtector =
        dataProtectionProvider.CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose);

    /// <summary>
    ///     This field contains the protector for two-factor recovery codes, with the purpose
    ///     <c>NinlilDnsUserStore</c> uses.
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
    ///     This method builds and broadcasts one user envelope on a detached task, logging any
    ///     exception other than cancellation, like the other config-sync subscribers.
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

            // The account was deleted before this task ran; the delete has its own event.
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
