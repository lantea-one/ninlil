using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.RewriteSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="RewriteSettingsDetail" />, as
///     returned by <see cref="Controllers.RewriteSettingsController.Get" />.
/// </summary>
public class RewriteSettingsDetailExample : IExamplesProvider<RewriteSettingsDetail>
{
    /// <summary>
    ///     This method returns rewrites turned on with the default 300-second answer TTL.
    /// </summary>
    public RewriteSettingsDetail GetExamples() => new(Enable: true, TimeToLiveSeconds: 300);
}
