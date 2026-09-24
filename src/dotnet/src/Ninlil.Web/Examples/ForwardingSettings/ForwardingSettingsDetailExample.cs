using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ForwardingSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ForwardingSettingsDetail" />, referenced by
///     <see cref="Controllers.ForwardingSettingsController.Get" />.
/// </summary>
public class ForwardingSettingsDetailExample : IExamplesProvider<ForwardingSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-opted-in configuration: forwarding on,
    ///     the health-checked round-robin failover strategy (this class's own default), and this
    ///     class's own compiled-in default timeouts and cache settings.
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
