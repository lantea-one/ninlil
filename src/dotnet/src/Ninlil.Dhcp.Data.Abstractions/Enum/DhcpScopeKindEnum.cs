using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Enum;

/// <summary>
///     This enumeration says what a <see cref="Entity.DhcpScopeEntity" /> actually hands out.
///     Meaningful only for a DHCPv6 scope (<c>DhcpScopeEntity.ProtocolVersion</c> ==
///     <c>IPv6</c>) -- a DHCPv4 scope always hands out ordinary addresses, RFC 8415 Prefix
///     Delegation having no DHCPv4 analogue at all, so every DHCPv4 scope is
///     <see cref="AddressAssignment" /> and this property is simply never consulted for one.
///     Serialized as a real string enum via <see cref="EnumDescriptionJsonConverter{TEnum}" />,
///     the same convention <c>InternetProtocolVersionEnum</c>/<c>LeaseStateEnum</c> already
///     establish (applied directly to the enum type itself, unlike
///     <c>Dhcpv4TagMatchModeEnum</c>'s own per-property attribute, so Swashbuckle's schema
///     generation correctly reflects it as a string union rather than a numeric one).
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DhcpScopeKindEnum>>]
public enum DhcpScopeKindEnum
{
    /// <summary>
    ///     An ordinary address pool: DHCPv4's own only mode, and RFC 8415's stateful address
    ///     assignment (IA_NA) for DHCPv6 -- a scope's own
    ///     <see cref="Entity.DhcpScopeEntity.RangeStartAddress" />/<c>RangeEndAddress</c> bound a
    ///     pool of individual addresses, one of which is ever handed to a given client at a time.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "AddressAssignment")]
    AddressAssignment = 0,

    /// <summary>
    ///     RFC 8415 Section 21.21's Prefix Delegation (IA_PD) -- a scope's own
    ///     <see cref="Entity.DhcpScopeEntity.RangeStartAddress" />/<c>RangeEndAddress</c> bound a
    ///     pool of delegable prefix blocks, each <see cref="Entity.DhcpScopeEntity.DelegatedPrefixLength" />
    ///     bits long, one of which is ever delegated to a given client (typically a downstream
    ///     router) at a time. IPv6-only; never meaningful when
    ///     <c>DhcpScopeEntity.ProtocolVersion</c> is <c>IPv4</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "PrefixDelegation")]
    PrefixDelegation = 1,
}
