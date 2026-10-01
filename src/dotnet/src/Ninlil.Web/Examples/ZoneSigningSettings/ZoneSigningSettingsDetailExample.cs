using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ZoneSigningSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="ZoneSigningSettingsDetail" />, as
///     returned by <see cref="Controllers.ZoneSigningSettingsController.Get" />.
/// </summary>
public class ZoneSigningSettingsDetailExample : IExamplesProvider<ZoneSigningSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults of
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Dnssec.ZoneSigningOptions" />.
    /// </summary>
    public ZoneSigningSettingsDetail GetExamples() =>
        new(
            TimeSpan.FromDays(30),
            TimeSpan.FromHours(1),
            TimeSpan.FromDays(5),
            TimeSpan.FromHours(6)
        );
}
