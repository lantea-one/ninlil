using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This class is the sending-side subscriber for <see cref="UserAccountChangedEvent" />. It
///     sends a user account's identity data, role memberships and direct permissions to every
///     healthy peer (see <see cref="ClusterConfigSyncSubscriberBase{TEntity}" /> for the shared
///     flow).
///     <para>
///         <see cref="UserClusterConfigSyncSource" /> builds the payload. It decrypts the two-factor
///         secrets with this node's key ring, the only one that can, and sends them in plaintext
///         over the TLS channel (see <see cref="UserSyncPayload" />). A secret that cannot be
///         decrypted fails the live broadcast, which is logged as an error with the user's id.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope, and the source, per event.</param>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a broadcast failure is recorded to.</param>
public class UserClusterSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<UserClusterSyncEventSubscriber> logger
)
    : ClusterConfigSyncSubscriberBase<UserEntity>(
        scopeFactory,
        broadcaster,
        clusterOptions,
        logger
    ),
        IDnsServerEventSubscriber<UserAccountChangedEvent>
{
    /// <inheritdoc />
    protected override string EntityType => UserClusterConfigSyncApplier.EntityTypeName;

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
}
