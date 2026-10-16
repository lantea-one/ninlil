// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This interface runs one cloud sync of a zone now, outside <see cref="CloudDnsZoneSyncService" />'s
///     schedule, so a caller that just changed a CloudPrimary zone (the ACME DNS-01 responder) need
///     not wait for the zone's <c>CloudSyncInterval</c>. <see cref="CloudDnsZoneSyncService" />
///     implements it, and the dependency injection container hands out its one running instance.
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
}
