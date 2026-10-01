using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="ClusterNodeDetail" />, as
///     returned by <see cref="Controllers.ClusterController.List" />.
/// </summary>
public class ClusterNodeDetailPagedResultExample : IExamplesProvider<PagedResult<ClusterNodeDetail>>
{
    /// <summary>
    ///     This method returns a page with a healthy peer and an unreachable one that has missed
    ///     five heartbeats.
    /// </summary>
    public PagedResult<ClusterNodeDetail> GetExamples() =>
        new(
            [
                new ClusterNodeDetail(
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
                ),
                new ClusterNodeDetail(
                    Guid.Parse("6f708192-a3b4-4c5d-d6e7-f8091a2b3c4d"),
                    "ninlil-basement",
                    Guid.Parse("70819293-a4b5-4c5d-e6f7-8091a2b3c4d5"),
                    "192.168.1.44",
                    443,
                    "cluster-b2c3d4e5f647890a1b2c3d4e5f647890",
                    "2e1c0b9a8d7e6f5a4b3c2d1e0f9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f0b",
                    ClusterNodeStatusEnum.Unreachable,
                    DateTimeOffset.Parse("2026-09-04T13:58:02+00:00"),
                    5,
                    null,
                    DateTimeOffset.Parse("2026-08-15T09:00:00+00:00"),
                    DateTimeOffset.Parse("2026-09-04T14:03:00+00:00")
                ),
            ],
            0,
            50,
            2
        );
}
