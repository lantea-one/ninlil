// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the secondary zone refresh settings
///     (<c>SecondaryZoneRefreshOptions</c>) that <c>GET /api/secondary-zone-refresh-settings</c>
///     returns. A <c>PATCH</c> to the same endpoint accepts the same fields.
/// </summary>
/// <param name="PollInterval">How often the background loop checks whether a secondary zone is due for a refresh. Defaults to 15 seconds.</param>
/// <param name="DefaultRetryInterval">How long to wait before retrying a zone with no local SOA (so no Retry value), whose primaries were all unreachable, or that is a stub zone. Defaults to 30 seconds.</param>
public record SecondaryZoneRefreshSettingsDetail(
    TimeSpan PollInterval,
    TimeSpan DefaultRetryInterval
);
