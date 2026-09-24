// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This interface lets something outside the secondary-zone refresh scheduler itself --
///     concretely, <see cref="NotifyQueryResolver" />, once it has validated an inbound NOTIFY --
///     ask it to check a specific zone right away, rather than waiting for that zone's own
///     refresh timer to come around on its own.
/// </summary>
public interface ISecondaryZoneRefreshTrigger
{
    /// <summary>
    ///     This method requests that a zone be checked against its primary as soon as possible.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to check.</param>
    void RequestImmediateRefresh(Guid zoneId);
}
