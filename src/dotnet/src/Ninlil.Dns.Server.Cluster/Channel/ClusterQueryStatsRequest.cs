// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This class is the body <c>ClusterQueryStatsAggregator</c> sends when asking one peer for
///     its own live query-stats snapshot. The receiving <c>ClusterChannelController.QueryStats</c>
///     action needs nothing from this body to decide whether to answer -- a valid TSIG signature
///     over the request is the entire trust decision, exactly like every other cluster-channel
///     action -- but <see cref="RequesterNodeId" /> lets the receiving node log which peer asked,
///     useful for diagnosing an unexpected request pattern.
/// </summary>
/// <param name="RequesterNodeId">The asking node's own stable <c>ClusterOptions.NodeId</c>.</param>
public record ClusterQueryStatsRequest(Guid RequesterNodeId);
