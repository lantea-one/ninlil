using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.AccessControl;

/// <summary>
///     This record is the JSON shape a <c>DnsAccessControlRuleEntity</c>'s own config-sync
///     envelope carries.
/// </summary>
public sealed record AccessControlRuleSyncPayload(
    string Name,
    string Cidr,
    DnsAccessControlCapabilityEnum Capabilities,
    bool IsDisabled
);
