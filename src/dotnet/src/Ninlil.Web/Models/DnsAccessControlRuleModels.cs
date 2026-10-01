using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace an access-control rule for a
///     network (<c>POST /api/access-control-rules</c>, <c>PUT /api/access-control-rules/{id}</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable name for the rule (for example, "Trusted LAN").</param>
/// <param name="Cidr">
///     The network the rule applies to, in CIDR notation with a prefix length (a bare address is
///     rejected; write <c>/32</c> or <c>/128</c> for one host). It must parse with
///     <see cref="System.Net.IPNetwork.TryParse(string, out System.Net.IPNetwork)" />, the parser
///     <c>DnsAccessControlRuleCache</c> matches with at query time.
/// </param>
/// <param name="Capabilities">
///     The capabilities the rule grants a client whose source address is in <see cref="Cidr" />,
///     as an array of <see cref="DnsAccessControlCapabilityEnum" /> member names (for example
///     <c>["RecursionAccess", "RateLimitBypass"]</c>), not the stored
///     <see cref="System.FlagsAttribute" /> bitmask. Names are case-sensitive. An empty array is
///     accepted (a rule that grants nothing); an unknown name is rejected with a 400.
/// </param>
/// <param name="IsDisabled">Whether the rule is disabled.</param>
public record DnsAccessControlRuleWriteRequest(
    string Name,
    string Cidr,
    IReadOnlyList<string> Capabilities,
    bool IsDisabled = false
);

/// <summary>
///     This class carries an access-control rule, as returned by the list, get and write
///     endpoints of <c>/api/access-control-rules</c>. Nothing in it is secret, so there is no
///     reveal-once handling as with <c>TsigKeyDetail</c> or <c>CloudProviderCredentialDetail</c>.
/// </summary>
/// <param name="Id">The rule's identifier.</param>
/// <param name="Name">The rule's human-readable name.</param>
/// <param name="Cidr">The rule's network in CIDR notation, as stored.</param>
/// <param name="Capabilities">
///     The capabilities the rule grants, as an array of
///     <see cref="DnsAccessControlCapabilityEnum" /> member names (see
///     <see cref="DnsAccessControlRuleWriteRequest.Capabilities" />).
/// </param>
/// <param name="IsDisabled">
///     Whether the rule is disabled. The enforcement engine ignores a disabled rule (see
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsAccessControlRuleEntity.IsDisabled" />).
/// </param>
/// <param name="CreatedAtUtc">When the rule was created.</param>
/// <param name="UpdatedAtUtc">When the rule was last modified, or <see langword="null" /> if never.</param>
/// <param name="OriginatingNodeId">
///     The cluster node the rule was last created or modified on, used by cluster config sync,
///     or <see langword="null" /> if it was never written with clustering enabled.
/// </param>
public record DnsAccessControlRuleDetail(
    Guid Id,
    string Name,
    string Cidr,
    IReadOnlyList<string> Capabilities,
    bool IsDisabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    Guid? OriginatingNodeId = null
);
