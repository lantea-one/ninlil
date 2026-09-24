using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.RewriteSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="RewriteSettingsDetail" />, referenced by
///     <see cref="Controllers.RewriteSettingsController.Get" />.
/// </summary>
public class RewriteSettingsDetailExample : IExamplesProvider<RewriteSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-opted-in configuration: rewrites on,
    ///     this class's own compiled-in default synthesized-answer TTL.
    /// </summary>
    public RewriteSettingsDetail GetExamples() => new(Enable: true, TimeToLiveSeconds: 300);
}
