// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when <c>SignedZoneCache</c> (Server.Authoritative) finishes
///     signing a zone, whether after a <c>DnsZoneChangedEvent</c>, a scheduled re-sign before
///     signatures expire (<c>DnssecResigningBackgroundService</c>) or the initial load at
///     startup. Webhooks can subscribe to it.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the zone finished re-signing.</param>
/// <param name="ZoneId">The identifier of the zone that was signed.</param>
/// <param name="ZoneName">The name of the zone that was signed.</param>
/// <param name="SignatureExpiresAtUtc">
///     When the new RRSIGs expire; <c>DnssecResigningBackgroundService</c> uses the same value
///     to schedule the next re-sign.
/// </param>
public record DnssecZoneResigningCompletedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ZoneId,
    string ZoneName,
    DateTimeOffset SignatureExpiresAtUtc
) : IDnsServerEvent;
