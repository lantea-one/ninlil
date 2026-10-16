// Define our namespace.
namespace Ninlil.Dhcp.Common.OptionCatalog;

/// <summary>
///     This record describes one DHCPv4 or DHCPv6 option in <see cref="Dhcpv4OptionCatalog" /> or
///     <see cref="Dhcpv6OptionCatalog" />: its IANA tag or code, a name and description, the shape
///     of its value (see <see cref="DhcpOptionValueTypeEnum" />), and whether "use this server's
///     address" makes sense as its value. It is metadata only: option delivery
///     (<c>Ninlil.Dhcp.Server.Core.Options.*</c>) never reads it. <c>DhcpOptionCatalogController</c>
///     serves it so the admin console can offer typed value entry for scope and tag-rule options.
/// </summary>
/// <param name="Code">
///     The option's tag or code as carried on the wire: 0-255 for a DHCPv4 tag
///     (<c>Dhcpv4Option.Tag</c>), 0-65535 for a DHCPv6 code (the RFC 8415 2-octet option code).
///     It matches the <see cref="Ninlil.Dhcp.Common.Enum.Dhcpv4OptionTagEnum" /> or
///     <see cref="Ninlil.Dhcp.Common.Enum.Dhcpv6OptionCodeEnum" /> member when there is one.
/// </param>
/// <param name="Name">A short name for the option, matching the defining RFC's section title.</param>
/// <param name="Description">
///     A longer description of what the option carries and which RFC section defines it, written
///     from the RFC text under the clean-room rule in <c>NOTICE.md</c>, never from another DHCP
///     server's documentation.
/// </param>
/// <param name="ValueType">The shape of the option's value, as its defining RFC gives it.</param>
/// <param name="IsServerAddressEligible">
///     Whether the UI offers "use this server's address" instead of a typed value (see
///     <see cref="Dhcpv4OptionCatalog" /> for the reasoning). It is only <see langword="true" />
///     when <see cref="ValueType" /> is address-shaped or
///     <see cref="DhcpOptionValueTypeEnum.String" /> (the address is then sent as text; see
///     <c>Dhcpv4OptionCatalog.EncodeServerAddress</c>). The address types are
///     (<see cref="DhcpOptionValueTypeEnum.IPv4Address" />,
///     <see cref="DhcpOptionValueTypeEnum.IPv4AddressList" />,
///     <see cref="DhcpOptionValueTypeEnum.IPv6Address" /> or
///     <see cref="DhcpOptionValueTypeEnum.IPv6AddressList" />); <c>DhcpOptionCatalogTests</c>
///     checks this.
/// </param>
public sealed record DhcpOptionCatalogEntry(
    int Code,
    string Name,
    string Description,
    DhcpOptionValueTypeEnum ValueType,
    bool IsServerAddressEligible
);
