using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes what happened to a DHCP lease when
///     <c>Server.Core.Events.DhcpLeaseChangedEvent</c> is published for it. Apart from the lease
///     identifier it is the only data the event carries; subscribers such as
///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsRegistrationService</c> re-read the lease from
///     <c>ILeaseRepository</c>, as with other id-only events like <c>DnsFilterListChangedEvent</c>
///     and <c>UserAccountChangedEvent</c>.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DhcpLeaseChangeKindEnum>>]
public enum DhcpLeaseChangeKindEnum
{
    /// <summary>
    ///     The lease was confirmed (a DHCPv4 ACK, RFC 2131 Section 4.3.2) or renewed, and is now
    ///     <see cref="LeaseStateEnum.Active" />. It fires on every confirmation, including a renewal
    ///     of an active lease, because a renewal can carry a changed host name or, after a scope's
    ///     range changed, a changed address.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Granted")]
    Granted = 0,

    /// <summary>
    ///     The client gave the address back before its lease expired (a DHCPv4 RELEASE, RFC 2131
    ///     Section 4.3.4). The lease is now <see cref="LeaseStateEnum.Released" />.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Released")]
    Released = 1,

    /// <summary>
    ///     The lease's validity period ran out without a renewal. A periodic sweep detects this,
    ///     because neither DHCPv4 nor DHCPv6 has a message a client sends when its lease expires.
    ///     It is also published when a client declines the address (DHCPv4 DECLINE, RFC 2131
    ///     Section 4.3.3): the lease row becomes a quarantine row that no client holds.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Expired")]
    Expired = 2,
}
