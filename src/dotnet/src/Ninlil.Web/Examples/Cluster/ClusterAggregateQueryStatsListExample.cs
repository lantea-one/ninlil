using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Cluster;

/// <summary>
///     This class supplies the Swagger example for the response of
///     <see cref="Controllers.ClusterController.Stats" />: the answering node's row, a reachable
///     peer with counts, and an unreachable peer listed with every count <see langword="null" />
///     rather than left out.
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
