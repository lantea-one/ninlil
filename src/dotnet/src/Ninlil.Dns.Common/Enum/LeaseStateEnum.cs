using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes where a DHCP lease is in its lifecycle, from the moment an
///     address is tentatively set aside for a client through to it being handed back to the pool.
///     The states mirror the client/server exchange described for DHCPv4 by RFC 2131 and for
///     DHCPv6 by RFC 8415; both protocols move a lease through the same conceptual stages even
///     though their wire messages differ.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<LeaseStateEnum>>]
public enum LeaseStateEnum
{
    /// <summary>
    ///     The server has tentatively offered this address to a client (DHCPv4 <c>DHCPOFFER</c> or
    ///     DHCPv6 <c>Advertise</c>), but the client has not yet confirmed it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Offered")]
    Offered = 0,

    /// <summary>
    ///     The client has confirmed the address and is actively using it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Active")]
    Active = 1,

    /// <summary>
    ///     The lease's validity period has elapsed without the client renewing it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Expired")]
    Expired = 2,

    /// <summary>
    ///     The client explicitly gave the address back before its lease expired (DHCPv4
    ///     <c>DHCPRELEASE</c> or DHCPv6 <c>Release</c>).
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Released")]
    Released = 3,

    /// <summary>
    ///     An administrator permanently reserved this address for a specific client, outside the
    ///     normal offer/lease lifecycle.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Reserved")]
    Reserved = 4,
}
