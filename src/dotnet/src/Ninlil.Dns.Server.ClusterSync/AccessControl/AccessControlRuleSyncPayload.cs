using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.AccessControl;

/// <summary>
///     This record is the JSON payload of a <c>DnsAccessControlRuleEntity</c> config-sync
///     envelope.
/// </summary>
public sealed record AccessControlRuleSyncPayload(
    string Name,
    string Cidr,
    DnsAccessControlCapabilityEnum Capabilities,
    bool IsDisabled
);
