using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration names how a tag-based conditional option delivery rule
///     (<c>Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity</c>) compares an opaque,
///     client-supplied text value -- its Vendor Class Identifier (RFC 2132 Section 9.13, option
///     60) or a User Class instance (RFC 3004, option 77) -- against the operator-configured
///     value a rule is looking for. Neither RFC mandates one particular comparison strategy; both
///     values are simply opaque strings a client chooses for itself, so this project defines its
///     own small, independently-designed matching vocabulary rather than assuming a single
///     "the whole thing must match" rule is always what an operator wants (a fleet of similar
///     devices often share a common vendor-class prefix -- "MSFT 5.0", "udhcp 1.30.1", and
///     similar -- while trailing characters vary by firmware build).
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
    ///     The client-supplied value must equal the configured value exactly, byte-for-byte.
    /// </summary>
    Exact = 0,

    /// <summary>
    ///     The client-supplied value must start with the configured value; anything after that
    ///     prefix is ignored.
    /// </summary>
    Prefix = 1,
}
