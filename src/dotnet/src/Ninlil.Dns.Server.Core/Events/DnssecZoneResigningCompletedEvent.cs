// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever <c>Server.Authoritative</c>'s own <c>SignedZoneCache</c>
///     finishes (re-)signing a zone -- whether triggered by a fresh <c>DnsZoneChangedEvent</c>
///     (an ordinary record edit), a proactive re-sign ahead of signature expiration
///     (<c>DnssecResigningBackgroundService</c>), or this process's own initial zone load at
///     startup -- a genuinely noteworthy DNSSEC-maintenance moment, so an outbound webhook
///     subscriber can be notified of it.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the zone finished re-signing.</param>
/// <param name="ZoneId">The identifier of the zone that was (re-)signed.</param>
/// <param name="ZoneName">The name of the zone that was (re-)signed.</param>
/// <param name="SignatureExpiresAtUtc">
///     The UTC date and time the freshly computed RRSIG set itself expires -- the same value
///     <c>DnssecResigningBackgroundService</c> uses to decide when this zone is next due for a
///     proactive re-sign.
/// </param>
public record DnssecZoneResigningCompletedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ZoneId,
    string ZoneName,
    DateTimeOffset SignatureExpiresAtUtc
) : IDnsServerEvent;
