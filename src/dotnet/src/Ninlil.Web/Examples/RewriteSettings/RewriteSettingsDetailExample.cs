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
    ///     This method returns rewrites turned on with the default 300-second answer TTL, last
    ///     written by one cluster node.
    /// </summary>
    public RewriteSettingsDetail GetExamples() =>
        new(
            Enable: true,
            TimeToLiveSeconds: 300,
            LastModifiedUtc: new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero),
            OriginatingNodeId: Guid.Parse("3f2a8c1e-5b7d-4e9a-8c61-0d4f2b9a7e13")
        );
}
