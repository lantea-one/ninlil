using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.RateLimiting;

/// <summary>
///     This class supplies the Swagger example for <see cref="RateLimitingSettingsDetail" />, as
///     returned by <see cref="Controllers.RateLimitingSettingsController.Get" />.
/// </summary>
public class RateLimitingSettingsDetailExample : IExamplesProvider<RateLimitingSettingsDetail>
{
    /// <summary>
    ///     This method returns the settings after the <c>Baseline</c> preset (see
    ///     <see cref="Ninlil.Dns.Server.Presets.DnsServerPresetCatalog.Baseline" />): rate limiting
    ///     on, with every limit at its default.
    /// </summary>
    public RateLimitingSettingsDetail GetExamples() =>
        new(
            true,
            20,
            40,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            100_000,
            24,
            56,
            TimeSpan.FromSeconds(10),
            500,
            100,
            50,
            []
        );
}
