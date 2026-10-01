using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilteringSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="FilteringSettingsDetail" />, as
///     returned by <see cref="Controllers.FilteringSettingsController.Get" />.
/// </summary>
public class FilteringSettingsDetailExample : IExamplesProvider<FilteringSettingsDetail>
{
    /// <summary>
    ///     This method returns filtering turned on with NXDOMAIN block responses; the other fields
    ///     are the defaults.
    /// </summary>
    public FilteringSettingsDetail GetExamples() =>
        new(
            Enable: true,
            BlockResponseMode: DnsFilterBlockResponseModeEnum.NxDomain,
            SinkholeAddressV4: "0.0.0.0",
            SinkholeAddressV6: "::",
            SinkholeTimeToLiveSeconds: 60,
            Precedence: DnsFilterListPrecedenceEnum.AllowWins,
            BlockCnameCloaking: true
        );
}
