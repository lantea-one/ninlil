using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes what actually happened to a DHCP lease when
///     <c>Server.Core.Events.DhcpLeaseChangedEvent</c> is published for it -- the one piece of
///     information a subscriber (most notably the DHCP-&gt;DNS integration feature,
///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c>) needs beyond the
///     lease's own identifier, since the event carries no other lease data (re-read fresh from
///     <c>ILeaseRepository</c> instead, the same "id-only event" shape
///     <c>DnsFilterListChangedEvent</c>/<c>UserAccountChangedEvent</c> already establish).
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DhcpLeaseChangeKindEnum>>]
public enum DhcpLeaseChangeKindEnum
{
    /// <summary>
    ///     The lease was newly confirmed (a DHCPv4 ACK, RFC 2131 Section 4.3.2) or renewed --
    ///     <see cref="LeaseStateEnum.Active" />. Fired on every confirmation, including a plain
    ///     renewal of an already-active lease, since a renewal can still carry a changed host
    ///     name or (after a scope's own range changed) address.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Granted")]
    Granted = 0,

    /// <summary>
    ///     The client voluntarily gave the address back before its lease expired (a DHCPv4
    ///     RELEASE, RFC 2131 Section 4.3.6) -- <see cref="LeaseStateEnum.Released" />.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Released")]
    Released = 1,

    /// <summary>
    ///     The lease's validity period elapsed without the client renewing it -- detected by a
    ///     periodic sweep rather than any client-originated message, since neither DHCPv4 nor
    ///     DHCPv6 has a wire message a client sends to announce its own expiration.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Expired")]
    Expired = 2,
}
