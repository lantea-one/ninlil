using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClusterInviteCreateResponse" />, referenced by
///     <see cref="Controllers.ClusterController.CreateInvite" />.
/// </summary>
public class ClusterInviteCreateResponseExample : IExamplesProvider<ClusterInviteCreateResponse>
{
    /// <summary>
    ///     This method returns a freshly created, still-valid invite -- the real opaque code is
    ///     shortened here for readability; a real one is considerably longer, carrying an entire
    ///     base64url-encoded JSON payload.
    /// </summary>
    public ClusterInviteCreateResponse GetExamples() =>
        new(
            "ninlil-cluster-invite-1.eyJWZXJzaW9uIjoxLCJJbnZpdGVyTm9kZUlkIjoiNGQ1ZTZmNzAtODE5Mi00YTNiLWI0YzUtZDZlN2Y4MDkxYTJiIiwuLi59",
            DateTimeOffset.Parse("2026-09-04T14:47:00+00:00")
        );
}
