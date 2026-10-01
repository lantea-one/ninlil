using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AdminLoginRateLimit;

/// <summary>
///     This class supplies the Swagger example for
///     <see cref="AdminLoginRateLimitSettingsDetail" />, as returned by
///     <see cref="Controllers.AdminLoginRateLimitSettingsController.Get" />.
/// </summary>
public class AdminLoginRateLimitSettingsDetailExample
    : IExamplesProvider<AdminLoginRateLimitSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: enabled, 5 attempts per 5-minute window.
    /// </summary>
    public AdminLoginRateLimitSettingsDetail GetExamples() => new(true, 5, TimeSpan.FromMinutes(5));
}
