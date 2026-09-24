using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClusterNodeDetail" />, referenced by
///     <see cref="Controllers.ClusterController.Get" />/<see cref="Controllers.ClusterController.Join" />.
/// </summary>
public class ClusterNodeDetailExample : IExamplesProvider<ClusterNodeDetail>
{
    /// <summary>
    ///     This method returns a healthy peer, mid-cluster, with a real recent heartbeat.
    /// </summary>
    public ClusterNodeDetail GetExamples() =>
        new(
            Guid.Parse("4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b"),
            "ninlil-office",
            Guid.Parse("5e6f7081-92a3-4b4c-c5d6-e7f8091a2b3c"),
            "10.0.1.20",
            443,
            "cluster-a1b2c3d4e5f647890a1b2c3d4e5f6478",
            "3f2b1a9c8d7e6f5a4b3c2d1e0f9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a",
            ClusterNodeStatusEnum.Healthy,
            DateTimeOffset.Parse("2026-09-04T14:32:10+00:00"),
            0,
            "Office router, reached over the site-to-site VPN.",
            DateTimeOffset.Parse("2026-09-01T10:00:00+00:00"),
            DateTimeOffset.Parse("2026-09-04T14:32:10+00:00")
        );
}
