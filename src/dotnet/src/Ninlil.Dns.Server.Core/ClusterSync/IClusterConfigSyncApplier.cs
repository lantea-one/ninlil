// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This interface is implemented once per operational-config category the cluster's own
///     config sync covers (filter lists, filter list entries, access-control rules, rewrites,
///     forwarders, settings sections) -- the receiving half of a sync. <c>ClusterChannelController</c>'s
///     own <c>config-sync</c> action resolves every registered <see cref="IClusterConfigSyncApplier" />
///     and dispatches an inbound <see cref="ClusterConfigSyncEnvelope" /> to whichever one's
///     <see cref="EntityType" /> matches, exactly the same "define the abstraction, implement it
///     once per real concern, dispatch by a discriminator" shape this project's
///     <c>IDnsServerEventSubscriber{TEvent}</c> registrations already establish for the event bus
///     itself.
///     <para>
///         Every real implementation owns its own last-write-wins decision internally (via
///         <see cref="ClusterSyncConflictResolver.IncomingWins" />): looking up whatever it
///         already has locally for <see cref="ClusterConfigSyncEnvelope.EntityKey" />, comparing
///         its own recorded timestamp/originating-node-id against the envelope's, and either
///         applying the incoming change through the exact same repository/write path a real local
///         REST write already uses, or silently discarding a stale envelope. Nothing about the
///         dispatch layer itself (<c>ClusterChannelController</c>) understands or enforces this --
///         it exists once, per category, inside each applier, precisely because only that
///         category's own applier knows how to read "what do I already have."
///     </para>
/// </summary>
public interface IClusterConfigSyncApplier
{
    /// <summary>
    ///     This property contains the entity-type discriminator this applier handles -- matched
    ///     against an inbound <see cref="ClusterConfigSyncEnvelope.EntityType" /> exactly, case
    ///     sensitively.
    /// </summary>
    string EntityType { get; }

    /// <summary>
    ///     This method applies one inbound change, if (and only if) it is genuinely newer than
    ///     whatever this node already has locally for the same entity -- see this interface's own
    ///     doc comment for the shared shape every implementation follows.
    /// </summary>
    /// <param name="envelope">The change to consider applying.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task ApplyAsync(ClusterConfigSyncEnvelope envelope, CancellationToken stoppingToken);
}
