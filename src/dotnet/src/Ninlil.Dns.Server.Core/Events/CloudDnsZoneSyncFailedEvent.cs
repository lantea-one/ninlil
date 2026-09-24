// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever <c>Server.Authoritative</c>'s own <c>CloudDnsZoneSyncService</c>
///     fails to reconcile a <c>CloudPrimary</c>/<c>CloudSecondary</c> zone against its configured
///     cloud DNS provider (an expired token, a provider outage, a misconfigured zone ID), so an
///     outbound webhook subscriber can be alerted to a sync failure rather than that failure only
///     ever being logged.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the sync attempt failed.</param>
/// <param name="ZoneId">The identifier of the zone whose sync attempt failed.</param>
/// <param name="ZoneName">The name of the zone whose sync attempt failed.</param>
/// <param name="ErrorMessage">A short, human-readable description of why the sync attempt failed.</param>
public record CloudDnsZoneSyncFailedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ZoneId,
    string ZoneName,
    string ErrorMessage
) : IDnsServerEvent;
