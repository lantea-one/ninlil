using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClusterJoinRequest" />, referenced by
///     <see cref="Controllers.ClusterController.Join" />.
/// </summary>
public class ClusterJoinRequestExample : IExamplesProvider<ClusterJoinRequest>
{
    /// <summary>
    ///     This method returns an invite code pasted from an inviting node's own "Cluster" page,
    ///     with a display name chosen for the new peer row this consumption creates locally.
    /// </summary>
    public ClusterJoinRequest GetExamples() =>
        new(
            "ninlil-cluster-invite-1.eyJWZXJzaW9uIjoxLCJJbnZpdGVyTm9kZUlkIjoiNGQ1ZTZmNzAtODE5Mi00YTNiLWI0YzUtZDZlN2Y4MDkxYTJiIiwuLi59",
            "ninlil-office"
        );
}
