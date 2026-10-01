// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Forwarding;

/// <summary>
///     This record is the JSON payload of a <c>DnsForwarderEntity</c> config-sync envelope.
/// </summary>
public sealed record ForwarderSyncPayload(
    string Name,
    string DefinitionString,
    int SortOrder,
    bool IsDisabled,
    string Comments
);
