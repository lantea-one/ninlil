using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class keeps <see cref="SignedZoneCache" /> in sync with the database: whenever
///     something changes a zone's records, this re-signs just that one zone -- mirroring
///     <see cref="Cache.ZoneChangedEventSubscriber" />'s own reload of the ordinary,
///     unsigned <see cref="Cache.AuthoritativeZoneCache" />, since re-signing is comparably cheap,
///     purely local CPU work with no network I/O to retry, unlike
///     <see cref="Notify.ZoneNotifyEventSubscriber" />'s detached-task pattern.
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
