// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the secondary zone refresh settings
///     (<c>SecondaryZoneRefreshOptions</c>) that <c>GET /api/secondary-zone-refresh-settings</c>
///     returns. A <c>PATCH</c> to the same endpoint accepts the same fields.
/// </summary>
/// <param name="PollInterval">How often the background loop checks whether a secondary zone is due for a refresh. Defaults to 15 seconds.</param>
/// <param name="DefaultRetryInterval">How long to wait before the next check of a zone with no local SOA yet (so no Retry value) or of a stub zone. Any other zone whose primaries were all unreachable is retried after its own SOA Retry value. Defaults to 30 seconds.</param>
public record SecondaryZoneRefreshSettingsDetail(
    TimeSpan PollInterval,
    TimeSpan DefaultRetryInterval
);
