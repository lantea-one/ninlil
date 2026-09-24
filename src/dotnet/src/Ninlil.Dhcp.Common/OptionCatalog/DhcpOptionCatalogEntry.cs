// Define our namespace.
namespace Ninlil.Dhcp.Common.OptionCatalog;

/// <summary>
///     This record names one DHCPv4 or DHCPv6 option <see cref="Dhcpv4OptionCatalog" />/
///     <see cref="Dhcpv6OptionCatalog" /> knows about: its real IANA-assigned tag/code number, a
///     human-readable name and description, the shape of value its defining RFC gives it (see
///     <see cref="DhcpOptionValueTypeEnum" />), and whether "use this server's own address" is a
///     sensible value source for it (see <see cref="IsServerAddressEligible" />'s own doc comment).
///     This is metadata only -- nothing in this project's real option encoding/decoding or delivery
///     code (<c>Ninlil.Dhcp.Server.Core.Options.*</c>) reads a catalog entry; the admin console uses it to
///     offer typed, operator-facing value entry for scope and tag-rule options.
/// </summary>
/// <param name="Code">
///     The option's own raw numeric tag/code, the exact value carried on the wire -- 0-255 for a
///     DHCPv4 tag (RFC 2132's own <c>Dhcpv4Option.Tag</c> byte), 0-65535 for a DHCPv6 code (RFC
///     8415's own 2-octet option-code field). For an option this project already gives a
///     strongly-typed accessor to, this is the exact same numeric value as the matching
///     <see cref="Ninlil.Dhcp.Common.Enum.Dhcpv4OptionTagEnum" />/
///     <see cref="Ninlil.Dhcp.Common.Enum.Dhcpv6OptionCodeEnum" /> member.
/// </param>
/// <param name="Name">A short, human-readable name for this option, matching its defining RFC's own section title.</param>
/// <param name="Description">
///     A longer, human-readable description of what this option carries, and which RFC/section
///     defines it -- sourced directly from that RFC's own text, per this project's clean-room rule
///     (see <c>NOTICE.md</c>), never from a copyleft competitor's own documentation or option
///     naming.
/// </param>
/// <param name="ValueType">The shape of value this option's own defining RFC gives it.</param>
/// <param name="IsServerAddressEligible">
///     Whether an operator picking this option's value should be offered "use this server's own
///     address" as an alternative to typing one in -- see <see cref="Dhcpv4OptionCatalog" />'s own
///     doc comment for the full eligibility reasoning. Always <see langword="false" /> unless
///     <see cref="ValueType" /> is itself address-shaped (<see cref="DhcpOptionValueTypeEnum.IPv4Address" />/
///     <see cref="DhcpOptionValueTypeEnum.IPv4AddressList" />/<see cref="DhcpOptionValueTypeEnum.IPv6Address" />/
///     <see cref="DhcpOptionValueTypeEnum.IPv6AddressList" />) -- a real, enforced invariant (see
///     this catalog's own committed test coverage), not merely a convention.
/// </param>
public sealed record DhcpOptionCatalogEntry(
    int Code,
    string Name,
    string Description,
    DhcpOptionValueTypeEnum ValueType,
    bool IsServerAddressEligible
);
