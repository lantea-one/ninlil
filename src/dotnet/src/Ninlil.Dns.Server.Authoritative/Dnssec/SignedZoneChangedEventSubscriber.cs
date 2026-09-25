using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class keeps <see cref="SignedZoneCache" /> in step with the database: on each
///     <see cref="DnsZoneChangedEvent" /> it reloads (re-signs or re-reads) that zone. Like
///     <see cref="Cache.ZoneChangedEventSubscriber" />, it runs inline on the event bus, since
///     signing is local CPU work; <see cref="Notify.ZoneNotifyEventSubscriber" /> instead
///     detaches because it does network I/O.
/// </summary>
/// <param name="signedZoneCache">The cache to keep in sync.</param>
public class SignedZoneChangedEventSubscriber(SignedZoneCache signedZoneCache)
    : IDnsServerEventSubscriber<DnsZoneChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsZoneChangedEvent domainEvent,
        CancellationToken stoppingToken
    ) => new(signedZoneCache.ReloadZoneAsync(domainEvent.ZoneId, stoppingToken));
}
