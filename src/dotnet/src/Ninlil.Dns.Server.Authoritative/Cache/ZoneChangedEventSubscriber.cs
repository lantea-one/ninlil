using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class keeps <see cref="AuthoritativeZoneCache" /> in sync with the database: whenever
///     something -- the REST API, a pulled zone transfer, dynamic update -- publishes a
///     <see cref="DnsZoneChangedEvent" />, this reloads just that one zone rather than the whole
///     cache.
/// </summary>
/// <param name="zoneCache">The cache to keep in sync.</param>
public class ZoneChangedEventSubscriber(AuthoritativeZoneCache zoneCache)
    : IDnsServerEventSubscriber<DnsZoneChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsZoneChangedEvent domainEvent,
        CancellationToken stoppingToken
    ) => new(zoneCache.ReloadZoneAsync(domainEvent.ZoneId, stoppingToken));
}
