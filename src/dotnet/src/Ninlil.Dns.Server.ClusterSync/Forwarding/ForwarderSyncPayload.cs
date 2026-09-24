// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Forwarding;

/// <summary>
///     This record is the JSON shape a <c>DnsForwarderEntity</c>'s own config-sync envelope
///     carries.
/// </summary>
public sealed record ForwarderSyncPayload(
    string Name,
    string DefinitionString,
    int SortOrder,
    bool IsDisabled,
    string Comments
);
