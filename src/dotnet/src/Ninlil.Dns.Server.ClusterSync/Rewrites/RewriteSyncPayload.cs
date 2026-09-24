// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Rewrites;

/// <summary>
///     This record is the JSON shape a <c>DnsRewriteEntity</c>'s own config-sync envelope
///     carries.
/// </summary>
public sealed record RewriteSyncPayload(
    string Domain,
    string Target,
    bool IsDisabled,
    string Comments
);
