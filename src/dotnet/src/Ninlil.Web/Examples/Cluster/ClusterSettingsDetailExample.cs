using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClusterSettingsDetail" />, as
///     returned by <see cref="Controllers.ClusterSettingsController.Get" />.
/// </summary>
public class ClusterSettingsDetailExample : IExamplesProvider<ClusterSettingsDetail>
{
    /// <summary>
    ///     This method returns clustering enabled with an advertised host and the default heartbeat
    ///     timing. The advertised port is still the default 0, which invites cannot use; it must be
    ///     set before invites work. A new install shows <c>enable: false</c> and an empty
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
