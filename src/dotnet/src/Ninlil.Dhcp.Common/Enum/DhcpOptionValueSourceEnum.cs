using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration names where a configured DHCP option's delivered value comes from.
///     <c>Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeCustomOptionEntity</c> and
///     <c>Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleOptionEntity</c> both carry one next to
///     their stored <c>ValueHex</c>. <see cref="Custom" /> is the zero value, so a row that leaves
///     it unset delivers its stored raw value unchanged.
///     <para>
///         The REST API sends this as its member name. The converter sits on the type rather than
///         on each DTO property so the OpenAPI document describes it as a string enum too:
///         Swashbuckle reads a type-level converter but ignores a property-level one.
///     </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DhcpOptionValueSourceEnum>))]
public enum DhcpOptionValueSourceEnum
{
    /// <summary>
    ///     The option's stored <c>ValueHex</c> is decoded from hexadecimal and delivered as-is.
    ///     This is the default.
    /// </summary>
    Custom = 0,

    /// <summary>
    ///     The stored <c>ValueHex</c> is ignored at delivery time and this server's address is
    ///     encoded and delivered instead. Which address each protocol substitutes is described on
    ///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv4OptionOverlayService</c> and
    ///     <c>Dhcpv6OptionOverlayService</c>. It only makes sense for an address-shaped option
    ///     (see
    ///     <see cref="Ninlil.Dhcp.Common.OptionCatalog.DhcpOptionCatalogEntry.IsServerAddressEligible" />),
    ///     but nothing enforces that: an uncataloged tag can use it too, and the server
    ///     substitutes its address the same way.
    /// </summary>
    ServerAddress = 1,
}
