using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Enum;

/// <summary>
///     This enumeration says what a <see cref="Entity.DhcpScopeEntity" /> hands out. It only
///     matters for a DHCPv6 scope: prefix delegation has no DHCPv4 counterpart, so every DHCPv4
///     scope is <see cref="AddressAssignment" />. It is serialized as a string with
///     <see cref="EnumDescriptionJsonConverter{TEnum}" />, like <c>InternetProtocolVersionEnum</c>
///     and <c>LeaseStateEnum</c>; the converter sits on the type so Swashbuckle describes it as a
///     string enum.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DhcpScopeKindEnum>>]
public enum DhcpScopeKindEnum
{
    /// <summary>
    ///     An address pool: the only DHCPv4 mode, and RFC 8415 stateful address assignment
    ///     (IA_NA) for DHCPv6. <see cref="Entity.DhcpScopeEntity.RangeStartAddress" /> and
    ///     <c>RangeEndAddress</c> bound a pool of single addresses.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "AddressAssignment")]
    AddressAssignment = 0,

    /// <summary>
    ///     RFC 8415 Section 21.21 prefix delegation (IA_PD), IPv6 only.
    ///     <see cref="Entity.DhcpScopeEntity.RangeStartAddress" /> and <c>RangeEndAddress</c> bound
    ///     a pool of prefixes, each <see cref="Entity.DhcpScopeEntity.DelegatedPrefixLength" />
    ///     bits long, delegated to clients (usually downstream routers).
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "PrefixDelegation")]
    PrefixDelegation = 1,
}
