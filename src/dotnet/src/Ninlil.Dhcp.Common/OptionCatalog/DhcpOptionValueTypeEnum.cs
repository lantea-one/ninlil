using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Dhcp.Common.OptionCatalog;

/// <summary>
///     This enumeration categorizes the wire-level shape of value a DHCP option's own defining RFC
///     gives it -- the small, deliberately reusable vocabulary <see cref="Dhcpv4OptionCatalog" />/
///     <see cref="Dhcpv6OptionCatalog" /> tag every catalog entry with, so a future value-entry UI
///     (a later sub-phase) can pick the right editor control for a given option without needing to
///     know that option's own RFC by name. This is metadata only -- nothing in this project's real
///     option encoding/decoding or delivery code reads this enumeration today; a future sub-phase
///     building genuine typed delivery on top of this catalog is expected to switch on it.
///     <para>
///         Serialized as a real string enum via the plain BCL <see cref="JsonStringEnumConverter{TEnum}" />
///         (the same converter <c>Ninlil.Web.Models.DhcpServiceKindEnum</c> already uses) rather
///         than <c>Ninlil.Common.Extension.Converter.EnumDescriptionJsonConverter{TEnum}</c> --
///         this project's own <c>Ninlil.Dhcp.Common</c> is deliberately dependency-free (BCL only,
///         see this project's own <c>.csproj</c> description), and <c>System.Text.Json</c> ships
///         in the shared framework itself, needing no extra package reference to use here.
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
    ///     A human-readable text value -- for most such options, RFC 2132/8415 describe this as "a
    ///     string" (NVT ASCII, not null-terminated). A few catalog entries carrying a delimited list
    ///     of domain names (RFC 3646's Domain Search List, for example) are also categorized this
    ///     way, deferring the real RFC 1035 §3.1 domain-name wire encoding those options actually use
    ///     to whichever future sub-phase implements genuine delivery for them.
    /// </summary>
    String,

    /// <summary>
    ///     A single-octet boolean flag, conventionally <c>1</c> for true / <c>0</c> for false (RFC
    ///     2132's own convention for every boolean-shaped option it defines).
    /// </summary>
    Boolean,

    /// <summary>A single unsigned 8-bit (1-octet) integer.</summary>
    UInt8,

    /// <summary>A single unsigned 16-bit (2-octet) integer, network byte order.</summary>
    UInt16,

    /// <summary>A single unsigned 32-bit (4-octet) integer, network byte order.</summary>
    UInt32,

    /// <summary>
    ///     An unsigned 32-bit (4-octet) integer counting seconds, network byte order -- wire-format
    ///     identical to <see cref="UInt32" />, categorized separately because this value is always
    ///     naturally a duration (an operator-facing value-entry UI should offer a duration/time-span
    ///     control here, not a bare integer spinner).
    /// </summary>
    Duration,

    /// <summary>
    ///     Raw, opaque bytes with no further structure this catalog's own small vocabulary
    ///     represents -- either because the option's own RFC-defined wire shape is itself genuinely
    ///     unstructured (RFC 2132 §8.4's Vendor Specific Information), or because it nests its own
    ///     further sub-structure (a tag/length/value list, or several distinct sub-fields) this
    ///     catalog deliberately does not attempt to decompose. The same "raw bytes as hex text"
    ///     convention <c>DhcpScopeCustomOptionEntity.ValueHex</c> already establishes applies to any
    ///     value actually entered for an option of this type.
    /// </summary>
    OpaqueHex,
}
