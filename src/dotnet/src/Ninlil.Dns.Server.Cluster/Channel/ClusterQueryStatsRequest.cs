// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This record is the body <see cref="ClusterQueryStatsAggregator" /> sends to ask a peer for
///     its current query statistics. <c>ClusterChannelController.QueryStats</c> answers any
///     request with a valid signature, as every cluster-channel action does;
///     <see cref="RequesterNodeId" /> only lets the peer log who asked.
/// </summary>
/// <param name="RequesterNodeId">The asking node's <c>ClusterOptions.NodeId</c>.</param>
public record ClusterQueryStatsRequest(Guid RequesterNodeId);
