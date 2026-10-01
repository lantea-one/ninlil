using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a tag rule: criteria matched against an incoming DHCPv4 or DHCPv6
///     request and, on a match, a set of options (<see cref="DhcpTagRuleOptionEntity" />) laid
///     over the reply after the scope's standard options (and, for DHCPv4, its custom options).
///     It was designed from the RFC and IANA text only, under the clean-room rule.
///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv4OptionOverlayService</c> and
///     <c>Dhcpv6OptionOverlayService</c> evaluate the rules; <c>DhcpTagRulesController</c> manages
///     them.
///     <para>
///         <b>Protocol.</b> There is no protocol field. A rule with a <see cref="ScopeId" /> is
///         only evaluated for that scope's protocol. A global rule is evaluated by both engines,
///         and each engine checks only the criteria it understands: the DHCPv4 engine ignores
///         <see cref="RelayInterfaceIdHex" /> and <see cref="VendorClassEnterpriseNumber" />, the
///         DHCPv6 engine ignores <see cref="RelayAgentCircuitIdHex" />. A global rule whose
///         criteria all belong to one protocol therefore has no criteria for the other protocol
///         and matches every request of that protocol too.
///     </para>
///     <para>
///         <b>Criteria are combined with AND.</b> Every criterion with a value must match. A rule
///         with only a Vendor Class Identifier matches on that alone; a rule with a Vendor Class
///         Identifier and a Relay Agent Circuit ID matches only requests with both. A rule with
///         no criteria matches every client it is eligible for, which is a way to send an overlay
///         to a whole scope.
///     </para>
///     <para>
///         <b>Eligibility.</b> A <see cref="ScopeId" /> restricts the rule to clients resolved into
///         that scope; <see langword="null" /> makes it eligible in every scope, which suits
///         criteria such as a device family's Vendor Class Identifier that do not depend on the
///         network segment.
///     </para>
///     <para>
///         <b>Order and conflicts.</b> The eligible rules that match are applied in ascending
///         <see cref="Priority" />, then
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.EntityBase.CreatedAtUtc" />, then
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.EntityBase.Id" />, so the order is
///         always the same. Each rule's options replace earlier values for the same tag, so the
///         last one applied wins: of two matching rules that set the same tag, the one with the
///         higher <see cref="Priority" /> (or, on a tie, the one created later) wins, just as a
///         custom option wins over a standard option by being applied later. To control which
///         rule wins, give it the higher <see cref="Priority" />.
///     </para>
/// </summary>
public class DhcpTagRuleEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the rule's operator-chosen, descriptive name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property optionally restricts this rule to clients resolved into one specific
    ///     scope; <see langword="null" /> makes the rule eligible against a client resolved into
    ///     any scope. See the class summary.
    /// </summary>
    public Guid? ScopeId { get; set; }

    /// <summary>
    ///     This property contains the rule's evaluation order, ascending; a higher value is
    ///     applied later and wins a conflict (see the class summary).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///     This property indicates whether this rule is disabled. A disabled rule is kept but
    ///     never evaluated.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property optionally contains the vendor class value to match, compared per
    ///     <see cref="VendorClassIdentifierMatchMode" />. For DHCPv4 it is compared with the
    ///     Vendor Class Identifier (RFC 2132 Section 9.13, option 60); for DHCPv6 it matches if
    ///     any vendor-class-data instance of the Vendor Class option (RFC 8415 Section 21.16,
    ///     option 16) matches, optionally narrowed by <see cref="VendorClassEnterpriseNumber" />.
    ///     <see langword="null" /> or empty means the vendor class is not checked.
    /// </summary>
    public string VendorClassIdentifierMatch { get; set; }

    /// <summary>
    ///     This property contains how <see cref="VendorClassIdentifierMatch" /> is compared with
    ///     the request's vendor class values (exact by default).
    /// </summary>
    public Dhcpv4TagMatchModeEnum VendorClassIdentifierMatchMode { get; set; }

    /// <summary>
    ///     This property optionally contains the user class value to match, compared per
    ///     <see cref="UserClassMatchMode" />. A request matches if any of its User Class instances
    ///     matches, from the DHCPv4 User Class option (RFC 3004, option 77) or the DHCPv6 one (RFC
    ///     8415 Section 21.15, option 15). <see langword="null" /> or empty means the user class
    ///     is not checked.
    /// </summary>
    public string UserClassMatch { get; set; }

    /// <summary>
    ///     This property contains how <see cref="UserClassMatch" /> is compared with each of the
    ///     request's User Class instances (exact by default).
    /// </summary>
    public Dhcpv4TagMatchModeEnum UserClassMatchMode { get; set; }

    /// <summary>
    ///     This property optionally contains the Agent Circuit ID sub-option value (RFC 3046
    ///     Section 2.1, inside option 82) to require, as an uppercase hex string compared for
    ///     byte equality (case-insensitively as hex). The value is often a binary VLAN or port
    ///     identifier with no text form, so there is no prefix mode. A request that was not
    ///     relayed has no Circuit ID and does not match. <see langword="null" /> or empty means
    ///     the Circuit ID is not checked. Only the DHCPv4 engine checks it.
    /// </summary>
    public string RelayAgentCircuitIdHex { get; set; }

    /// <summary>
    ///     This property optionally contains a remote ID to require, encoded and compared like
    ///     <see cref="RelayAgentCircuitIdHex" />. The DHCPv4 engine compares it with the Agent
    ///     Remote ID sub-option (RFC 3046 Section 2.2, inside option 82) and the DHCPv6 engine
    ///     with the Remote-ID option (RFC 4649, option 37); both name the client or its attachment
    ///     point as the relay agent chose. A global rule is checked by both engines with the same
    ///     value.
    /// </summary>
    public string RelayAgentRemoteIdHex { get; set; }

    /// <summary>
    ///     This property optionally contains the Interface-ID option value (RFC 8415 Section
    ///     21.18) of a relayed DHCPv6 request to require, as an uppercase hex string compared for
    ///     byte equality. It plays the role <see cref="RelayAgentCircuitIdHex" /> plays for DHCPv4
    ///     (which relay interface the client is behind), though the options are unrelated on the
    ///     wire. A request that was not relayed, or whose relay sent no Interface-ID, does not
    ///     match. <see langword="null" /> or empty means it is not checked. Only
    ///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService</c> checks it.
    /// </summary>
    public string RelayInterfaceIdHex { get; set; }

    /// <summary>
    ///     This property optionally contains the IANA Enterprise Number the DHCPv6 Vendor Class
    ///     option (RFC 8415 Section 21.16, code 16) must carry. It is checked in addition to
    ///     <see cref="VendorClassIdentifierMatch" />, or on its own when that is empty (any vendor
    ///     class from this enterprise). <see langword="null" /> means any enterprise number. The
    ///     DHCPv4 Vendor Class Identifier has no enterprise number, so only
    ///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService</c> checks it.
    /// </summary>
    public uint? VendorClassEnterpriseNumber { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this rule.
    /// </summary>
    public string Comments { get; set; }
}
