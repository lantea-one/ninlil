using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration names how a tag rule
///     (<c>Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity</c>) compares a client-supplied
///     value, such as its Vendor Class Identifier (RFC 2132 Section 9.13, option 60) or a User
///     Class instance (RFC 3004, option 77), with the configured value. The RFCs leave
///     comparison to the server. Prefix matching exists because similar devices often share a
///     vendor-class prefix ("MSFT 5.0", "udhcp 1.30.1") while the rest varies by firmware build.
///     <para>
///         The REST API sends this as its member name. The converter sits on the type rather than
///         on each DTO property so the OpenAPI document describes it as a string enum too:
///         Swashbuckle reads a type-level converter but ignores a property-level one.
///     </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Dhcpv4TagMatchModeEnum>))]
public enum Dhcpv4TagMatchModeEnum
{
    /// <summary>
    ///     The client-supplied value must equal the configured value byte for byte.
    /// </summary>
    Exact = 0,

    /// <summary>
    ///     The client-supplied value must start with the configured value; anything after that
    ///     prefix is ignored.
    /// </summary>
    Prefix = 1,
}
