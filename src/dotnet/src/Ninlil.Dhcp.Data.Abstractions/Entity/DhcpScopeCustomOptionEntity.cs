using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents an operator-configured DHCPv4 option for a
///     <see cref="DhcpScopeEntity" />: a raw tag and value sent to every client of the scope, on
///     top of the standard options <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv4OptionDeliveryService</c>
///     computes (Subnet Mask, Router, Domain Name Server and so on). It covers options with no
///     dedicated scope field, such as vendor-specific options (RFC 2132 Section 8.4), NTP servers
///     or anything else a network needs. <c>DhcpScopeCustomOptionsController</c> manages the
///     rows and <c>Dhcpv4OptionOverlayService</c> delivers them. Only the DHCPv4 engine reads
///     them; the DHCPv6 engine does not deliver custom options.
///     <para>
///         <b>Precedence.</b> When a custom option uses the same tag as a standard option (tag 1,
///         Subnet Mask, for example), the custom option wins: the overlay service applies custom
///         options after the standard ones, replacing them.
///     </para>
///     <para>
///         <b>Parameter Request List.</b> The standard Subnet Mask, Router and Domain Name Server
///         options are filtered by the client's Parameter Request List (RFC 2131 Section 3.5
///         says a server "SHOULD" include the requested parameters it has). A custom option is
///         always sent, whether or not the list names its tag, because many embedded and PXE
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
    ///     This property contains the option tag (0-255), as in
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
