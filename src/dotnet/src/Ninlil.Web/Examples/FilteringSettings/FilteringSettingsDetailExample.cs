using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilteringSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="FilteringSettingsDetail" />, referenced by
///     <see cref="Controllers.FilteringSettingsController.Get" />.
/// </summary>
public class FilteringSettingsDetailExample : IExamplesProvider<FilteringSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-opted-in configuration: filtering on,
    ///     the classic Pi-hole-style NXDOMAIN block response, and this class's own compiled-in
    ///     default sinkhole addresses and precedence.
    /// </summary>
    public FilteringSettingsDetail GetExamples() =>
        new(
            Enable: true,
            BlockResponseMode: DnsFilterBlockResponseModeEnum.NxDomain,
            SinkholeAddressV4: "0.0.0.0",
            SinkholeAddressV6: "::",
            SinkholeTimeToLiveSeconds: 60,
            Precedence: DnsFilterListPrecedenceEnum.AllowWins
        );
}
