using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Dhcp.Common.OptionCatalog;

/// <summary>
///     This enumeration names the wire shape of a DHCP option's value. Every entry in
///     <see cref="Dhcpv4OptionCatalog" /> and <see cref="Dhcpv6OptionCatalog" /> carries one, so
///     the admin console can choose a value editor and encode the typed value as hex without
///     knowing each option's RFC. Server-side option delivery never reads it; it sends the stored
///     hex.
///     <para>
///         It is serialized as a string with the BCL <see cref="JsonStringEnumConverter{TEnum}" />
///         rather than <c>Ninlil.Common.Extension.Converter.EnumDescriptionJsonConverter{TEnum}</c>,
///         because <c>Ninlil.Dhcp.Common</c> depends on the BCL only and
///         <c>System.Text.Json</c> is part of the shared framework.
///     </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DhcpOptionValueTypeEnum>))]
public enum DhcpOptionValueTypeEnum
{
    /// <summary>A single IPv4 address, encoded as 4 octets.</summary>
    IPv4Address,

    /// <summary>One or more IPv4 addresses packed back-to-back, each 4 octets, in preference order.</summary>
    IPv4AddressList,

    /// <summary>A single IPv6 address, encoded as 16 octets.</summary>
    IPv6Address,

    /// <summary>One or more IPv6 addresses packed back-to-back, each 16 octets, in preference order.</summary>
    IPv6AddressList,

    /// <summary>
    ///     A text value; RFC 2132 and RFC 8415 describe most of these as NVT ASCII strings with no
    ///     terminating null. A few entries that carry a list of domain names (RFC 3646's Domain
    ///     Search List, for example) are also tagged this way, although on the wire those options
    ///     use RFC 1035 Section 3.1 domain-name encoding, which nothing here produces.
    /// </summary>
    String,

    /// <summary>
    ///     A single-octet boolean flag: <c>1</c> for true, <c>0</c> for false (the RFC 2132
    ///     convention).
    /// </summary>
    Boolean,

    /// <summary>A single unsigned 8-bit (1-octet) integer.</summary>
    UInt8,

    /// <summary>A single unsigned 16-bit (2-octet) integer, network byte order.</summary>
    UInt16,

    /// <summary>A single unsigned 32-bit (4-octet) integer, network byte order.</summary>
    UInt32,

    /// <summary>
    ///     An unsigned 32-bit (4-octet) integer counting seconds, network byte order. It is
    ///     encoded like <see cref="UInt32" />; the separate value tells the UI the number is a
    ///     duration.
    /// </summary>
    Duration,

    /// <summary>
    ///     Raw bytes, entered as hex. Used when the option is unstructured (RFC 2132 §8.4's Vendor
    ///     Specific Information) or has a nested structure (a tag/length/value list or several
    ///     sub-fields) that the catalog does not break down. The value is stored as-is in
    ///     <c>DhcpScopeCustomOptionEntity.ValueHex</c>.
    /// </summary>
    OpaqueHex,
}
