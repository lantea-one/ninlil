// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when <c>CloudDnsZoneSyncService</c> (Server.Authoritative) fails
///     to reconcile a <c>CloudPrimary</c> or <c>CloudSecondary</c> zone with its cloud DNS
///     provider (an expired token, a provider outage, a wrong zone ID), so a webhook can raise
///     an alert instead of the failure only being logged.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the sync attempt failed.</param>
/// <param name="ZoneId">The identifier of the zone whose sync attempt failed.</param>
/// <param name="ZoneName">The name of the zone whose sync attempt failed.</param>
/// <param name="ErrorMessage">A short description of why the sync attempt failed.</param>
public record CloudDnsZoneSyncFailedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ZoneId,
    string ZoneName,
    string ErrorMessage
) : IDnsServerEvent;
