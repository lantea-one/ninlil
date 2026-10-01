using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClusterInviteCreateResponse" />, as
///     returned by <see cref="Controllers.ClusterController.CreateInvite" />.
/// </summary>
public class ClusterInviteCreateResponseExample : IExamplesProvider<ClusterInviteCreateResponse>
{
    /// <summary>
    ///     This method returns a new invite. The code is shortened for readability: a real one
    ///     carries the whole base64url-encoded JSON payload, including the shared secret, and is
    ///     much longer.
    /// </summary>
    public ClusterInviteCreateResponse GetExamples() =>
        new(
            "ninlil-cluster-invite-1.eyJWZXJzaW9uIjoxLCJJbnZpdGVyTm9kZUlkIjoiNGQ1ZTZmNzAtODE5Mi00YTNiLWI0YzUtZDZlN2Y4MDkxYTJiIiwuLi59",
            DateTimeOffset.Parse("2026-09-04T14:47:00+00:00")
        );
}
