using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.RateLimiting;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="RateLimitingSettingsDetail" />, referenced by
///     <see cref="Controllers.RateLimitingSettingsController.Get" />.
/// </summary>
public class RateLimitingSettingsDetailExample : IExamplesProvider<RateLimitingSettingsDetail>
{
    /// <summary>
    ///     This method returns the exact settings the <c>Baseline</c> preset itself applies (see
    ///     <see cref="Ninlil.Dns.Server.Presets.DnsServerPresetCatalog.Baseline" />), a realistic
    ///     moderate-hardening posture rather than an arbitrary value.
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
