using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClusterNodeWriteRequest" />, referenced by
///     <see cref="Controllers.ClusterController.Patch" />.
/// </summary>
public class ClusterNodeWriteRequestExample : IExamplesProvider<ClusterNodeWriteRequest>
{
    /// <summary>
    ///     This method returns a rename -- only <see cref="ClusterNodeWriteRequest.Name" /> and
    ///     <see cref="ClusterNodeWriteRequest.Comments" /> are ever editable; see that record's
    ///     own doc comment for why every other field is fixed once a peer row exists.
    /// </summary>
    public ClusterNodeWriteRequest GetExamples() =>
        new(
            "ninlil-office-2nd-floor",
            "Renamed after the office network was split into two VLANs."
        );
}
