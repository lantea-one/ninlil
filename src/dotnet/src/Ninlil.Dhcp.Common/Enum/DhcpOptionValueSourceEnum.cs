using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration names where a configured DHCP option's own delivered value actually comes
///     from -- <c>Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeCustomOptionEntity</c> and
///     <c>Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleOptionEntity</c> both carry one of
///     these alongside their own stored <c>ValueHex</c>. Every row that existed before this
///     property did (and every row a write path leaves unset) is <see cref="Custom" /> -- today's
///     only real behavior, and this enumeration's zero/default value for exactly that reason, so a
///     live deployment's existing custom options and tag-rule options keep delivering their own
///     already-configured raw value, byte-for-byte unchanged, across an upgrade that adds this
///     property.
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
    ///     The option's own stored <c>ValueHex</c> is delivered as-is, decoded from hexadecimal
    ///     into raw bytes -- the only value source that ever existed before this enumeration did,
    ///     and the default for every row.
    /// </summary>
    Custom = 0,

    /// <summary>
    ///     The option's own stored <c>ValueHex</c> is ignored entirely at delivery time; this
    ///     server's own real address (resolved fresh for the protocol/address family the reply
    ///     being built actually needs -- see
    ///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv4OptionOverlayService</c>/
    ///     <c>Dhcpv6OptionOverlayService</c>'s own doc comments for exactly which resolved address
    ///     each protocol substitutes) is encoded and delivered instead. Sensible only for an
    ///     address-shaped option -- see
    ///     <see cref="Ninlil.Dhcp.Common.OptionCatalog.DhcpOptionCatalogEntry.IsServerAddressEligible" />'s
    ///     own doc comment for the catalog's own eligibility guidance -- but nothing at the data or
    ///     delivery layer actually enforces that against the catalog: a raw, uncataloged tag
    ///     (this entity's whole reason to exist) can still be given this value source, and the
    ///     delivery engine substitutes this server's own address for it exactly the same way,
    ///     trusting the operator's own judgment the same way every other raw-tag/raw-value custom
    ///     option already does.
    /// </summary>
    ServerAddress = 1,
}
