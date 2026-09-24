using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AdminLoginRateLimit;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="AdminLoginRateLimitSettingsDetail" />, referenced by
///     <see cref="Controllers.AdminLoginRateLimitSettingsController.Get" />.
/// </summary>
public class AdminLoginRateLimitSettingsDetailExample
    : IExamplesProvider<AdminLoginRateLimitSettingsDetail>
{
    /// <summary>
    ///     This method returns this feature's own real, on-by-default settings.
    /// </summary>
    public AdminLoginRateLimitSettingsDetail GetExamples() => new(true, 5, TimeSpan.FromMinutes(5));
}
