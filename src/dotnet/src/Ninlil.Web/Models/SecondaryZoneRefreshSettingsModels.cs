// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>SecondaryZoneRefreshOptions</c> -- how often the background loop that checks whether a
///     secondary zone is due for a refresh wakes up. This record's own fields are the exact same
///     ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
public record SecondaryZoneRefreshSettingsDetail(
    TimeSpan PollInterval,
    TimeSpan DefaultRetryInterval
);
