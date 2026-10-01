using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClusterJoinRequest" />, the request
///     body of <see cref="Controllers.ClusterController.Join" />.
/// </summary>
public class ClusterJoinRequestExample : IExamplesProvider<ClusterJoinRequest>
{
    /// <summary>
    ///     This method returns the shortened invite code from
    ///     <see cref="ClusterInviteCreateResponseExample" />, pasted from the inviting node's
    ///     Cluster page, with a display name for the peer row the join creates.
    /// </summary>
    public ClusterJoinRequest GetExamples() =>
        new(
            "ninlil-cluster-invite-1.eyJWZXJzaW9uIjoxLCJJbnZpdGVyTm9kZUlkIjoiNGQ1ZTZmNzAtODE5Mi00YTNiLWI0YzUtZDZlN2Y4MDkxYTJiIiwuLi59",
            "ninlil-office"
        );
}
