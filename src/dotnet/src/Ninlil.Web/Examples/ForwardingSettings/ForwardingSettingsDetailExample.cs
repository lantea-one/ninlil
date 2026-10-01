using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ForwardingSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="ForwardingSettingsDetail" />, as
///     returned by <see cref="Controllers.ForwardingSettingsController.Get" />.
/// </summary>
public class ForwardingSettingsDetailExample : IExamplesProvider<ForwardingSettingsDetail>
{
    /// <summary>
    ///     This method returns forwarding turned on; every other field is the default.
    /// </summary>
    public ForwardingSettingsDetail GetExamples() =>
        new(
            Enable: true,
            FailoverStrategy: ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin,
            HealthCheckInterval: TimeSpan.FromSeconds(30),
            PerForwarderTimeout: TimeSpan.FromSeconds(3),
            EnableCache: true,
            MaxCacheEntries: 10000
        );
}
