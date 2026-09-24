using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClusterSettingsDetail" />, referenced by
///     <see cref="Controllers.ClusterSettingsController.Get" />.
/// </summary>
public class ClusterSettingsDetailExample : IExamplesProvider<ClusterSettingsDetail>
{
    /// <summary>
    ///     This method returns clustering enabled and fully configured -- a fresh install with
    ///     clustering never turned on would instead show <c>enable: false</c> and a blank
    ///     <c>advertisedHost</c>.
    /// </summary>
    public ClusterSettingsDetail GetExamples() =>
        new(
            true,
            Guid.Parse("4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b"),
            "10.0.1.10",
            0,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(10),
            3,
            TimeSpan.FromMinutes(1)
        );
}
