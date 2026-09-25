// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSubnet;

/// <summary>
///     This class binds to the <c>DnsServer:ClientSubnet</c> section of the application's
///     configuration and controls <see cref="EdnsClientSubnetService" />: whether and how this
///     server attaches an RFC 7871 EDNS Client Subnet option to the queries it sends while
///     resolving recursively or forwarding, so an authoritative server (usually a CDN) can tailor
///     its answer to roughly where the client is. It is read on every query.
/// </summary>
public sealed class EdnsClientSubnetOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:ClientSubnet";

    /// <summary>
    ///     This property controls whether outbound queries carry an ECS option. Off by default
    ///     because ECS has a privacy cost: it tells every upstream server part of the client's
    ///     network location.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains how many leading bits of an IPv4 address are sent upstream when
    ///     the option is built from the client's source address or
    ///     <see cref="OverrideAddress" />. It does not limit an ECS option the client supplied
    ///     (see <see cref="EdnsClientSubnetService" />). The default, 24, is the usual choice:
    ///     coarse enough to hide the individual address, fine enough for CDN geolocation.
    /// </summary>
    public byte SourcePrefixLengthIPv4 { get; set; } = 24;

    /// <summary>
    ///     This property contains the IPv6 counterpart to <see cref="SourcePrefixLengthIPv4" />.
    ///     The default, 56, is the usual choice; a /56 is typically one home or small site.
    /// </summary>
    public byte SourcePrefixLengthIPv6 { get; set; } = 56;

    /// <summary>
    ///     This property, when set, is sent in every outbound ECS option instead of any client's
    ///     address, cut to the prefix length for its family. It lets an operator advertise one
    ///     fixed location (such as the server's egress network) without disclosing any client's
    ///     subnet. <see langword="null" /> (the default) uses the client's address. An
    ///     unparseable value means no ECS option is sent.
    /// </summary>
    public string OverrideAddress { get; set; }
}
