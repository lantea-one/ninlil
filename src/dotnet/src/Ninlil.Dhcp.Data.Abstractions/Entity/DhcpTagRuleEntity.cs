using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single tag-based conditional option delivery rule -- an
///     independently designed mechanism (sourced only from RFC/IANA text, never from any
///     competing implementation's own code, per this project's clean-room rule) that matches an
///     inbound DHCPv4 <em>or</em> DHCPv6 request against one or more criteria and, on a match,
///     overlays its own option set
///     (<see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleOptionEntity" />) on top of
///     whatever a scope's own base standard (and, for DHCPv4, custom) options already computed.
///     Which protocol's own request a rule is ever actually evaluated against follows directly
///     from <see cref="ScopeId" />'s own protocol (a scope-restricted rule only ever sees that
///     scope's own protocol's requests; a globally-eligible rule is evaluated by both
///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv4OptionOverlayService</c> and
///     <c>Dhcpv6OptionOverlayService</c> alike, with each engine simply never finding its own
///     protocol's criteria set on a rule whose operator only ever configured the other protocol's
///     fields) -- there is no separate "protocol" flag on this entity, since the criteria
///     themselves already say which protocol's requests they can ever match at all.
///     <para>
///         <b>Match criteria are combined with AND, not OR</b>: every criterion this rule actually
///         has a non-empty value for must match the request for the rule as a whole to match. A
///         rule naming only a Vendor Class Identifier matches on that alone; a rule naming both a
///         Vendor Class Identifier and a Relay Agent Circuit ID only matches a request presenting
///         both. A rule with zero criteria set at all (only, at most, a <see cref="ScopeId" />)
///         matches every client eligible for that scope -- a deliberate, if unusual, way to send an
///         unconditional overlay to everyone in one scope without needing a whole separate scope
///         just to do so.
///     </para>
///     <para>
///         <b>Which clients a rule is even eligible to match</b>: <see cref="ScopeId" /> set to a
///         specific scope's identifier restricts this rule to clients resolved into that one
///         scope; left <see langword="null" />, the rule is eligible against a client resolved into
///         <em>any</em> scope -- useful for a criterion (a specific Vendor Class Identifier
///         representing one device family, for example) that is meaningful regardless of which
///         physical network segment the device happens to be on.
///     </para>
///     <para>
///         <b>Evaluation order and conflict resolution when multiple rules match the same
///         option tag</b>: every rule eligible for a client's own resolved scope (scoped to it, or
///         global) is evaluated, in ascending <see cref="Priority" /> order (ties broken by
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.EntityBase.CreatedAtUtc" /> ascending,
///         then <see cref="Ninlil.Dns.Data.Abstractions.Entity.EntityBase.Id" /> ascending, for a
///         fully deterministic total order even between two rules created in the same instant).
///         Every matching rule's own option overlay is then applied, in that same order, onto the
///         reply already carrying the scope's base standard+custom options -- so this is a
///         deliberate <em>last-applied-wins</em> scheme: of two matching rules that both configure
///         a value for the same option tag, the one with the higher <see cref="Priority" /> number
///         (or, tied on priority, the more recently created one) wins, exactly the same way a
///         scope's own custom option already wins over a standard option for the same tag by being
///         applied later. An operator who cares which of two competing rules should win for a
///         given client simply gives the one that should win the higher <see cref="Priority" />
///         number -- an explicit, inspectable value, rather than an incidental database row order
///         nothing can render legible after the fact.
///     </para>
/// </summary>
public class DhcpTagRuleEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the rule's own operator-chosen, descriptive name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property optionally restricts this rule to clients resolved into one specific
    ///     scope; <see langword="null" /> makes the rule eligible against a client resolved into
    ///     any scope. See this class's own doc comment for the full reasoning.
    /// </summary>
    public Guid? ScopeId { get; set; }

    /// <summary>
    ///     This property contains this rule's own evaluation order, ascending -- see this class's
    ///     own doc comment for the full evaluation-order and conflict-resolution rule this value
    ///     drives.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///     This property indicates whether this rule is currently disabled. A disabled rule is
    ///     kept on record but never evaluated against any request.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property optionally contains the Vendor Class Identifier value this rule matches a
    ///     request's own value against, per <see cref="VendorClassIdentifierMatchMode" /> --
    ///     against a DHCPv4 request, its single opaque Vendor Class Identifier option value (RFC
    ///     2132 Section 9.13, option 60); against a DHCPv6 request, matching if <em>any</em> one of
    ///     the Vendor Class option's own (possibly several) vendor-class-data instances (RFC 8415
    ///     Section 21.16, option 16) matches, optionally narrowed further by
    ///     <see cref="VendorClassEnterpriseNumber" />. <see langword="null" />/empty means this
    ///     rule does not consider a Vendor Class option at all.
    /// </summary>
    public string VendorClassIdentifierMatch { get; set; }

    /// <summary>
    ///     This property contains how <see cref="VendorClassIdentifierMatch" /> is compared against
    ///     a request's own Vendor Class Identifier/vendor-class-data value(s).
    /// </summary>
    public Dhcpv4TagMatchModeEnum VendorClassIdentifierMatchMode { get; set; }

    /// <summary>
    ///     This property optionally contains the User Class value this rule matches against a
    ///     request's own User Class instances, per <see cref="UserClassMatchMode" /> -- a request
    ///     matches if <em>any</em> one of its own (possibly several) User Class Data instances
    ///     matches, whether decoded from a DHCPv4 request's own User Class option (RFC 3004,
    ///     option 77) or a DHCPv6 request's own, structurally near-identical User Class option (RFC
    ///     8415 Section 21.15, option 15). <see langword="null" />/empty means this rule does not
    ///     consider a User Class option at all.
    /// </summary>
    public string UserClassMatch { get; set; }

    /// <summary>
    ///     This property contains how <see cref="UserClassMatch" /> is compared against each of a
    ///     request's own User Class instances.
    /// </summary>
    public Dhcpv4TagMatchModeEnum UserClassMatchMode { get; set; }

    /// <summary>
    ///     This property optionally contains the Agent Circuit ID sub-option value
    ///     (RFC 3046 Section 2.1, nested inside option 82) this rule requires, encoded as an
    ///     uppercase hexadecimal string, compared for exact equality against a relayed request's
    ///     own value -- neither RFC 3046 nor real-world relay agent practice defines a stable text
    ///     encoding for this value (it is often a raw VLAN/port identifier, not printable text), so
    ///     unlike the two text-based criteria above, only an exact byte match is offered.
    ///     <see langword="null" />/empty means this rule does not consider the Agent Circuit ID at
    ///     all -- including, correctly, a request that was never relayed and so carries no Relay
    ///     Agent Information option in the first place.
    /// </summary>
    public string RelayAgentCircuitIdHex { get; set; }

    /// <summary>
    ///     This property optionally contains the Agent Remote ID sub-option value
    ///     (RFC 3046 Section 2.2, nested inside option 82) this rule requires, encoded and compared
    ///     the same way <see cref="RelayAgentCircuitIdHex" /> is -- doing double duty as this
    ///     rule's own DHCPv6 Remote-ID criterion too (RFC 4649, option 37): both identify the
    ///     client, or its attachment point, as a relay agent itself chose to name it, compared the
    ///     same "exact byte match" way, and a rule is only ever eligible against one protocol's own
    ///     requests at a time (see <see cref="ScopeId" />'s own protocol-scoping), so there is no
    ///     real risk of the two protocols' values being confused with one another.
    /// </summary>
    public string RelayAgentRemoteIdHex { get; set; }

    /// <summary>
    ///     This property optionally contains the raw value bytes of a relayed DHCPv6 request's own
    ///     Interface-ID option (RFC 8415 Section 21.18), encoded as an uppercase hexadecimal
    ///     string and compared for exact equality -- the DHCPv6 analogue of
    ///     <see cref="RelayAgentCircuitIdHex" />'s own "which of a relay agent's own
    ///     interfaces/ports a client's message arrived on" purpose, though the two options are
    ///     defined by different RFCs and have no wire-format relationship to one another.
    ///     <see langword="null" />/empty means this rule does not consider it at all -- including,
    ///     correctly, a request that was never relayed, or was relayed by an agent that did not set
    ///     this optional option. Meaningless against a DHCPv4 request; only ever evaluated by
    ///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService</c>.
    /// </summary>
    public string RelayInterfaceIdHex { get; set; }

    /// <summary>
    ///     This property optionally contains the IANA Enterprise Number a DHCPv6 request's own
    ///     Vendor Class option (RFC 8415 Section 21.16, code 16) must carry for this rule to
    ///     consider matching it at all -- checked in addition to, not instead of,
    ///     <see cref="VendorClassIdentifierMatch" />/<see cref="VendorClassIdentifierMatchMode" />,
    ///     which (for a DHCPv6 request) is compared against <em>any</em> one of the option's own
    ///     vendor-class-data instances, the same "matches if any instance matches" shape
    ///     <see cref="UserClassMatch" /> already documents for the User Class option.
    ///     <see langword="null" /> means this rule does not require any particular enterprise
    ///     number -- only the vendor-class-data text itself is compared. Meaningless against a
    ///     DHCPv4 request, whose own Vendor Class Identifier option carries no enterprise number at
    ///     all; only ever evaluated by
    ///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService</c>.
    /// </summary>
    public uint? VendorClassEnterpriseNumber { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this rule.
    /// </summary>
    public string Comments { get; set; }
}
