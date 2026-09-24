using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a per-client/network
///     access-control rule (<c>POST</c>/<c>PUT /api/access-control-rules</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable name for this rule (for example, "Trusted LAN").</param>
/// <param name="Cidr">
///     The network this rule applies to, in CIDR notation (a bare address is a
///     <c>/32</c>/<c>/128</c> network of one address) -- validated against
///     <see cref="System.Net.IPNetwork.TryParse(string, out System.Net.IPNetwork)" />, the same
///     parser <see cref="Controllers.ClientGroupNetworksController" />'s own <c>TryValidateCidr</c>
///     already uses and <c>DnsAccessControlRuleCache</c> itself matches against at query time.
/// </param>
/// <param name="Capabilities">
///     Every capability this rule grants a client whose source address falls within
///     <see cref="Cidr" />, as an explicit array of <see cref="DnsAccessControlCapabilityEnum" />
///     member names (for example, <c>["RecursionAccess", "RateLimitBypass"]</c>) -- the projection
///     <see cref="DnsAccessControlCapabilityEnum" />'s own doc comment calls for, decoupling this
///     wire shape from the entity's own stored <see cref="System.FlagsAttribute" /> bitmask. An
///     empty array is accepted (a rule that currently grants nothing); an unrecognized name is
///     rejected with a validation error, never silently ignored.
/// </param>
/// <param name="IsDisabled">Whether the rule starts out (or is currently) disabled.</param>
public record DnsAccessControlRuleWriteRequest(
    string Name,
    string Cidr,
    IReadOnlyList<string> Capabilities,
    bool IsDisabled = false
);

/// <summary>
///     This class carries a single access-control rule's own fields, for list,
///     single-item, and creation responses. No secret material is involved anywhere in this
///     entity, so, unlike <c>TsigKeyDetail</c>/<c>DnssecKeyDetail</c>/
///     <c>CloudProviderCredentialDetail</c>, there is no reveal-once policy to apply here at all.
/// </summary>
/// <param name="Id">This rule's own identifier.</param>
/// <param name="Name">This rule's own human-readable name.</param>
/// <param name="Cidr">This rule's own network, in CIDR notation, exactly as stored.</param>
/// <param name="Capabilities">
///     Every capability this rule currently grants, as an explicit array of
///     <see cref="DnsAccessControlCapabilityEnum" /> member names -- see
///     <see cref="DnsAccessControlRuleWriteRequest.Capabilities" />'s own doc comment for why this
///     is not the raw stored bitmask.
/// </param>
/// <param name="IsDisabled">
///     Whether this rule is currently disabled -- a disabled rule is never consulted by the
///     enforcement engine, per <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsAccessControlRuleEntity.IsDisabled" />'s
///     own doc comment.
/// </param>
/// <param name="CreatedAtUtc">When this rule was created.</param>
/// <param name="UpdatedAtUtc">When this rule was last modified, or <see langword="null" /> if never.</param>
/// <param name="OriginatingNodeId">
///     The cluster node this rule was last created or modified on (the cluster's own operational
///     config sync), or <see langword="null" /> on a deployment that has never had clustering
///     enabled.
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
