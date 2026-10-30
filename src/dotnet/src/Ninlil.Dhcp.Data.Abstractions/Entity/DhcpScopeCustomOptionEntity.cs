using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents an operator-configured DHCP option for a
///     <see cref="DhcpScopeEntity" />: a raw tag (DHCPv6: option code) and value sent to every
///     client of the scope, on top of the standard options
///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv4OptionDeliveryService</c> or
///     <c>Dhcpv6OptionDeliveryService</c> computes (Subnet Mask, Router, Domain Name Server and
///     so on). It covers options with no
///     dedicated scope field, such as vendor-specific options (RFC 2132 Section 8.4), NTP servers
///     or anything else a network needs. <c>DhcpScopeCustomOptionsController</c> manages the
///     rows; <c>Dhcpv4OptionOverlayService</c> delivers them on a DHCPv4 scope and
///     <c>Dhcpv6OptionOverlayService</c> on a DHCPv6 scope (a tag rule setting the same code
///     wins over a custom option on both). On a DHCPv6 scope the option codes the server writes
///     itself (such as 2 Server Identifier) are refused when written and skipped on delivery.
///     <para>
///         <b>Precedence.</b> When a custom option uses the same tag as a standard option (tag 1,
///         Subnet Mask, for example), the custom option wins: the overlay service applies custom
///         options after the standard ones, replacing them (on DHCPv6, code 23 replaces the
///         scope's DNS servers).
///     </para>
///     <para>
///         <b>Parameter Request List.</b> The standard Subnet Mask, Router and Domain Name Server
///         options are filtered by the client's Parameter Request List (RFC 2131 Section 3.5
///         and RFC 2132 Section 9.8 let a client list the parameters it wants). A custom option is
///         always sent, whether or not the list (DHCPv6: the Option Request Option) names its tag, because many embedded and PXE
///         clients expect vendor or boot options they never request.
///     </para>
/// </summary>
public class DhcpScopeCustomOptionEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DhcpScopeEntity" /> this custom
    ///     option belongs to.
    /// </summary>
    public Guid ScopeId { get; set; }

    /// <summary>
    ///     This property contains the option tag (0-255; for a DHCPv6 scope the option code, which
    ///     must also be in that range), as in
    ///     <see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Option.Tag" />, whether or not it has a
    ///     <see cref="Ninlil.Dhcp.Common.Enum.Dhcpv4OptionTagEnum" /> member.
    /// </summary>
    public byte OptionTag { get; set; }

    /// <summary>
    ///     This property contains the option value as an uppercase hexadecimal string
    ///     (<see cref="System.Convert.ToHexString(byte[])" />). Hex rather than Base64 makes it
    ///     easy to compare with vendor documentation, which usually gives values in hex. It is
    ///     ignored when <see cref="ValueSource" /> is
    ///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" />, and may then be blank
    ///     (<c>DhcpScopeCustomOptionsController.TryValidate</c> allows that).
    /// </summary>
    public string ValueHex { get; set; }

    /// <summary>
    ///     This property indicates where the delivered value comes from (see
    ///     <see cref="DhcpOptionValueSourceEnum" />). The default,
    ///     <see cref="DhcpOptionValueSourceEnum.Custom" />, delivers <see cref="ValueHex" />.
    /// </summary>
    public DhcpOptionValueSourceEnum ValueSource { get; set; } = DhcpOptionValueSourceEnum.Custom;

    /// <summary>
    ///     This property indicates whether this custom option is disabled. A disabled custom
    ///     option is kept but not delivered.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this custom option.
    /// </summary>
    public string Comments { get; set; }
}
