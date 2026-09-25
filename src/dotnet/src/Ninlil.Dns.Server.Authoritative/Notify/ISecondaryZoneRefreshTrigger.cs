// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This interface lets <see cref="NotifyQueryResolver" />, after accepting a NOTIFY, ask the
///     secondary refresh scheduler (<c>Refresh.SecondaryZoneRefreshService</c>) to check a zone
///     now instead of waiting for its refresh timer.
/// </summary>
public interface ISecondaryZoneRefreshTrigger
{
    /// <summary>
    ///     This method requests that a zone be checked against its primary as soon as possible.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to check.</param>
    void RequestImmediateRefresh(Guid zoneId);
}
