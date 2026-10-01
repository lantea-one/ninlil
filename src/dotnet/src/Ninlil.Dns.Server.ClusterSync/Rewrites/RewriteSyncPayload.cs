// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Rewrites;

/// <summary>
///     This record is the JSON payload of a <c>DnsRewriteEntity</c> config-sync envelope.
/// </summary>
public sealed record RewriteSyncPayload(
    string Domain,
    string Target,
    bool IsDisabled,
    string Comments
);
