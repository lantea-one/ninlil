using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeyRolloverSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnssecKeyRolloverSettingsDetail" />, referenced by
///     <see cref="Controllers.DnssecKeyRolloverSettingsController.Get" />.
/// </summary>
public class DnssecKeyRolloverSettingsDetailExample
    : IExamplesProvider<DnssecKeyRolloverSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Dnssec.DnssecKeyRolloverOptions" />), not
    ///     arbitrary values.
    /// </summary>
    public DnssecKeyRolloverSettingsDetail GetExamples() =>
        new(TimeSpan.FromHours(1), TimeSpan.FromDays(1), TimeSpan.FromMinutes(15));
}
