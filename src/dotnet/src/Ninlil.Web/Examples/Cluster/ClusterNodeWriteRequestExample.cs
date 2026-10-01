using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClusterNodeWriteRequest" />, the
///     request body of <see cref="Controllers.ClusterController.Patch" />.
/// </summary>
public class ClusterNodeWriteRequestExample : IExamplesProvider<ClusterNodeWriteRequest>
{
    /// <summary>
    ///     This method returns a rename with a note. Only
    ///     <see cref="ClusterNodeWriteRequest.Name" /> and
    ///     <see cref="ClusterNodeWriteRequest.Comments" /> can be edited (see
    ///     <see cref="ClusterNodeWriteRequest" />).
    /// </summary>
    public ClusterNodeWriteRequest GetExamples() =>
        new(
            "ninlil-office-2nd-floor",
            "Renamed after the office network was split into two VLANs."
        );
}
