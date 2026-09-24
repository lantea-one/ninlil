using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.ClusterController.Stats" />: this node's own row, a healthy peer
///     reporting real counts, and an unreachable peer reported with every count
///     <see langword="null" /> rather than omitted.
/// </summary>
public class ClusterAggregateQueryStatsListExample
    : IExamplesProvider<IReadOnlyList<ClusterAggregateQueryStatsDetail>>
{
    /// <inheritdoc />
    public IReadOnlyList<ClusterAggregateQueryStatsDetail> GetExamples() =>
        [
            new ClusterAggregateQueryStatsDetail(
                null,
                "This node",
                true,
                true,
                412,
                9_845,
                128_004
            ),
            new ClusterAggregateQueryStatsDetail(
                Guid.Parse("4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b"),
                "ninlil-office",
                false,
                true,
                388,
                9_120,
                117_552
            ),
            new ClusterAggregateQueryStatsDetail(
                Guid.Parse("6f708192-a3b4-4c5d-e6f7-08091a2b3c4d"),
                "ninlil-cottage",
                false,
                false,
                null,
                null,
                null
            ),
        ];
}
