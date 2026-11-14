using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Cluster.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class is an <see cref="IOptionsMonitor{TOptions}" /> that reports one fixed
///     <see cref="ClusterOptions" /> and never changes. The applier tests pass it to the
///     appliers that read this node's id.
/// </summary>
/// <param name="nodeId">The node id the options report.</param>
internal sealed class FixedClusterOptionsMonitor(Guid nodeId) : IOptionsMonitor<ClusterOptions>
{
    private readonly ClusterOptions _value = new() { NodeId = nodeId, Enable = true };

    /// <inheritdoc />
    public ClusterOptions CurrentValue => _value;

    /// <inheritdoc />
    public ClusterOptions Get(string name) => _value;

    /// <inheritdoc />
    public IDisposable OnChange(Action<ClusterOptions, string> listener) => null;
}
