// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when a zone's contents or settings change: a record edit through
///     the REST API, a dynamic update, a completed zone transfer, and so on. Many subscribers
///     react to it, including the authoritative zone cache reload, the signed zone cache,
///     outbound RFC 1996 NOTIFY, PTR sync, catalog zones, forwarder zone sync, CAA
///     reconciliation, webhooks and the live events hub. The code that changed the zone does not
///     need to know about any of them.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the zone changed.</param>
/// <param name="ZoneId">The identifier of the zone that changed.</param>
/// <param name="ZoneName">The name of the zone that changed.</param>
public record DnsZoneChangedEvent(DateTimeOffset OccurredAtUtc, Guid ZoneId, string ZoneName)
    : IDnsServerEvent;
