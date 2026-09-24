// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever a zone's contents change -- a record added through the
///     REST API, an incoming dynamic update, or a freshly-completed zone transfer. Several
///     independent subscribers react to it -- reloading the in-memory zone cache, sending an
///     outbound RFC 1996 NOTIFY to the zone's secondary servers, re-signing a DNSSEC-signed zone,
///     and syncing implied PTR records -- without the code that made the change needing to know
///     anything about any of them.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the zone changed.</param>
/// <param name="ZoneId">The identifier of the zone that changed.</param>
/// <param name="ZoneName">The name of the zone that changed.</param>
public record DnsZoneChangedEvent(DateTimeOffset OccurredAtUtc, Guid ZoneId, string ZoneName)
    : IDnsServerEvent;
