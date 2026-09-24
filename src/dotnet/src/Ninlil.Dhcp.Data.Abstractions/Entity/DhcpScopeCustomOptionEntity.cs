using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single operator-configured, arbitrary DHCPv4 option -- a raw
///     option tag plus its raw value bytes -- sent to every client of a
///     <see cref="DhcpScopeEntity" /> alongside whatever standard options
///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv4OptionDeliveryService</c> already computes for it
///     (Subnet Mask, Router, Domain Name Server, and so on). This exists for the option tags this
///     project has no dedicated, strongly-typed mapping for at all -- a vendor-specific option
///     (RFC 2132 Section 8.4's own "site-specific"/vendor-extension range), NTP servers, TFTP
///     server name, or any other tag an operator's own network needs -- without this project
///     needing to grow a first-class accessor and scope-entity property for every conceivable
///     option before an operator can use it.
///     <para>
///         <b>Precedence over a standard option for the same tag</b>: when a scope's own custom
///         option happens to name the same tag one of the standard options already computes (for
///         example, tag 1, Subnet Mask), the custom option always wins. An operator who
///         deliberately configures a custom value for a tag that already has a standard mapping is
///         doing so specifically to override the standard-computed value, not to have it silently
///         discarded in favor of the value this project would have derived automatically --
///         <c>Dhcpv4OptionDeliveryService</c> applies every scope's own custom options strictly
///         after its standard ones for exactly this reason.
///     </para>
///     <para>
///         Unlike the three optional standard options (Subnet Mask/Router/Domain Name Server),
///         which honor a client's own Parameter Request List per RFC 2132 Section 3.5's "SHOULD
///         include" guidance for values a server merely happens to have on hand, a configured
///         custom option is always delivered regardless of whether the client's own Parameter
///         Request List names its tag. It exists because an operator explicitly decided a client
///         needs it -- many embedded/IoT/PXE-style clients never list a vendor-specific or
///         boot-related tag on their own Parameter Request List even though they still expect a
///         server to supply it, and a client's own incomplete request list must not silently
///         defeat deliberate operator configuration.
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
    ///     This property contains the option's own raw numeric tag (0-255) -- the same value RFC
    ///     2132/<see cref="Ninlil.Dhcp.Common.Wire.Dhcpv4Option.Tag" /> uses, whether or not this
    ///     project also happens to know the tag under a named <see cref="Ninlil.Dhcp.Common.Enum.Dhcpv4OptionTagEnum" />
    ///     member.
    /// </summary>
    public byte OptionTag { get; set; }

    /// <summary>
    ///     This property contains the option's own raw value bytes, encoded as an uppercase
    ///     hexadecimal string (<see cref="System.Convert.ToHexString(byte[])" />) -- the same
    ///     "raw bytes as hex text" convention this project's own DNSSEC key material already uses,
    ///     chosen over Base64 purely for how much easier a hex string is for an operator to
    ///     hand-verify against a vendor's own published option-value documentation (which is
    ///     itself almost always expressed in hex). Ignored entirely at delivery time when
    ///     <see cref="ValueSource" /> is <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> --
    ///     may be left blank in that case, and a blank value is deliberately tolerated by write-time
    ///     validation for exactly that reason (see <c>DhcpScopeCustomOptionsController</c>'s own
    ///     <c>TryValidate</c> method).
    /// </summary>
    public string ValueHex { get; set; }

    /// <summary>
    ///     This property indicates where this custom option's own delivered value actually comes
    ///     from -- see <see cref="DhcpOptionValueSourceEnum" />'s own doc comment. Defaults to
    ///     <see cref="DhcpOptionValueSourceEnum.Custom" />, the only value source that existed
    ///     before this property did, so every row created before this property existed keeps
    ///     delivering its own already-configured <see cref="ValueHex" /> unchanged.
    /// </summary>
    public DhcpOptionValueSourceEnum ValueSource { get; set; } = DhcpOptionValueSourceEnum.Custom;

    /// <summary>
    ///     This property indicates whether this custom option is currently disabled. A disabled
    ///     custom option is kept on record but not delivered to any client.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this custom option.
    /// </summary>
    public string Comments { get; set; }
}
