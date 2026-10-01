using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeyRolloverSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnssecKeyRolloverSettingsDetail" />,
///     as returned by <see cref="Controllers.DnssecKeyRolloverSettingsController.Get" />.
/// </summary>
public class DnssecKeyRolloverSettingsDetailExample
    : IExamplesProvider<DnssecKeyRolloverSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults of
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Dnssec.DnssecKeyRolloverOptions" />.
    /// </summary>
    public DnssecKeyRolloverSettingsDetail GetExamples() =>
        new(TimeSpan.FromHours(1), TimeSpan.FromDays(1), TimeSpan.FromMinutes(15));
}
