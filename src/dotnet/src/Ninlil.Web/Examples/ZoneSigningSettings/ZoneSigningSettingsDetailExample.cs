using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ZoneSigningSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ZoneSigningSettingsDetail" />, referenced by
///     <see cref="Controllers.ZoneSigningSettingsController.Get" />.
/// </summary>
public class ZoneSigningSettingsDetailExample : IExamplesProvider<ZoneSigningSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Dnssec.ZoneSigningOptions" />'s own doc comments), not
    ///     an arbitrary value.
    /// </summary>
    public ZoneSigningSettingsDetail GetExamples() =>
        new(
            TimeSpan.FromDays(30),
            TimeSpan.FromHours(1),
            TimeSpan.FromDays(5),
            TimeSpan.FromHours(6)
        );
}
