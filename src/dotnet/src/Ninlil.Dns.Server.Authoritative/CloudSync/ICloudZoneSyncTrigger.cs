// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This interface syncs one cloud zone outside <see cref="CloudDnsZoneSyncService" />'s
///     schedule, so a caller that just changed a CloudPrimary zone need not wait for the zone's
///     <c>CloudSyncInterval</c>. There are two ways: <see cref="SyncZoneNowAsync" /> waits for the
///     sync and reports its failure (the ACME DNS-01 responder uses it), and
///     <see cref="RequestSync" /> only asks and returns at once (the dynamic update resolver uses
///     it). <see cref="CloudDnsZoneSyncService" /> implements both, and the dependency injection
///     container hands out its one running instance.
/// </summary>
public interface ICloudZoneSyncTrigger
{
    /// <summary>
    ///     This method syncs one zone with its cloud provider now: a push for a CloudPrimary zone,
    ///     a pull for a CloudSecondary zone. It does nothing when the zone does not exist, is
    ///     disabled or is of another type. It waits for a sync already in progress, so two syncs
    ///     never overlap.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to sync.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="Exception">Any failure of the sync itself (provider outage, bad credential) propagates to the caller.</exception>
    Task SyncZoneNowAsync(Guid zoneId, CancellationToken stoppingToken);

    /// <summary>
    ///     This method asks for one zone to be synced soon and returns at once, without waiting for
    ///     the sync or for a sync already in progress. Requests for the same zone that arrive before
    ///     its sync starts are served by one sync, so a burst costs at most two syncs. A request for
    ///     an unknown, disabled or non-cloud zone is dropped. A failed sync is not reported to the
    ///     caller: it logs a warning, publishes <see cref="Ninlil.Dns.Server.Core.Events.CloudDnsZoneSyncFailedEvent" />
    ///     and the next sync (the next request or the zone's interval) retries.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to sync.</param>
    void RequestSync(Guid zoneId);
}
