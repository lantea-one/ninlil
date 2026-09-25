using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class keeps <see cref="AuthoritativeZoneCache" /> in step with the database: on each
///     <see cref="DnsZoneChangedEvent" /> (from the REST API, a zone transfer, a dynamic update
///     and so on) it reloads that one zone.
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
