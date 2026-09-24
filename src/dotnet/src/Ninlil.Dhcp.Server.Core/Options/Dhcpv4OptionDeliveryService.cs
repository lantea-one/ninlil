using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This static class applies a scope's own standard DHCPv4 configuration options onto an
///     outgoing reply message -- the Subnet Mask, Router, and Domain Name Server options RFC 2131
///     Section 4.3 expects a server to supply alongside an address, plus the Server Identifier
///     and (for OFFER/ACK, not INFORM) IP Address Lease Time options every reply carries.
///     <para>
///         RFC 2131 Section 3.5 describes a client's own Parameter Request List option (tag 55,
///         RFC 2132 Section 9.8) as naming "a list of the parameters that the client is
///         requesting"; per that same section, a server's own reply "SHOULD include" whichever of
///         those it has values for, but is not limited to only those. This method honors that
///         guidance by filtering its own optional (Subnet Mask/Router/Domain Name Server) options
///         against the client's own requested list when one was actually sent, and otherwise
///         includes every option this scope has a configured value for -- a client that sends no
///         Parameter Request List at all still receives every standard option this scope can
///         offer, rather than none.
///     </para>
/// </summary>
internal static class Dhcpv4OptionDeliveryService
{
    /// <summary>
    ///     This method applies a scope's own standard option values onto a reply message.
    /// </summary>
    /// <param name="reply">The reply message to apply options onto.</param>
    /// <param name="scope">The scope whose own configured option values should be applied.</param>
    /// <param name="request">
    ///     The original request, consulted for its own Parameter Request List (if any) and for
    ///     whether this reply includes a lease time at all.
    /// </param>
    /// <param name="serverIdentifier">This server's own Server Identifier address.</param>
    /// <param name="leaseDuration">
    ///     The lease duration to advertise via the IP Address Lease Time option, or
    ///     <see langword="null" /> to omit that option entirely -- RFC 2131 Section 4.3.5's own
    ///     DHCPINFORM reply carries no lease at all, since an INFORM client already has its own
    ///     address through some other means.
    /// </param>
    public static void Apply(
        Dhcpv4Message reply,
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress serverIdentifier,
        TimeSpan? leaseDuration
    )
    {
        reply.SetServerIdentifier(serverIdentifier);

        if (leaseDuration.HasValue)
            reply.SetIpAddressLeaseTime(leaseDuration.Value);

        bool requestedTagsPresent = request.TryGetParameterRequestList(
            out IReadOnlyList<byte> requestedTags
        );

        if (
            scope.SubnetPrefixLength is >= 0 and <= 32
            && IncludesOrNoPreference(
                requestedTagsPresent,
                requestedTags,
                Dhcpv4OptionTagEnum.SubnetMask
            )
        )
            reply.SetSubnetMask(ComputeSubnetMask(scope.SubnetPrefixLength));

        if (
            scope.GatewayAddresses.Count > 0
            && IncludesOrNoPreference(
                requestedTagsPresent,
                requestedTags,
                Dhcpv4OptionTagEnum.Router
            )
            && TryParseAll(scope.GatewayAddresses, out IReadOnlyList<IPAddress> gateways)
        )
            reply.SetRouters(gateways);

        if (
            scope.DnsServerAddresses.Count > 0
            && IncludesOrNoPreference(
                requestedTagsPresent,
                requestedTags,
                Dhcpv4OptionTagEnum.DomainNameServer
            )
            && TryParseAll(scope.DnsServerAddresses, out IReadOnlyList<IPAddress> dnsServers)
        )
            reply.SetDomainNameServers(dnsServers);

        ApplyPxeBootOptions(reply, scope, serverIdentifier, requestedTagsPresent, requestedTags);
    }

    /// <summary>
    ///     This method applies a scope's own PXE boot configuration, if any, onto a reply --
    ///     RFC 2131 §2's own fixed "file"/"siaddr" header fields unconditionally (a BOOTP header
    ///     field is always present on every reply, never subject to a client's own Parameter
    ///     Request List the way an RFC 2132 option is), plus the RFC 2132 §9.4/§9.5 Bootfile
    ///     Name/TFTP Server Name options when the client's own Parameter Request List asks for
    ///     them -- see <see cref="DhcpScopeEntity.BootFileName" />'s own doc comment for why both
    ///     mechanisms are offered together.
    /// </summary>
    private static void ApplyPxeBootOptions(
        Dhcpv4Message reply,
        DhcpScopeEntity scope,
        IPAddress serverIdentifier,
        bool requestedTagsPresent,
        IReadOnlyList<byte> requestedTags
    )
    {
        if (string.IsNullOrWhiteSpace(scope.BootFileName))
            return;

        IPAddress nextServerAddress = serverIdentifier;
        if (
            !string.IsNullOrWhiteSpace(scope.NextServerAddress)
            && IPAddress.TryParse(scope.NextServerAddress, out IPAddress configuredAddress)
        )
            nextServerAddress = configuredAddress;

        reply.BootFileName = scope.BootFileName;
        reply.ServerIpAddress = nextServerAddress;

        if (
            IncludesOrNoPreference(
                requestedTagsPresent,
                requestedTags,
                Dhcpv4OptionTagEnum.BootfileName
            )
        )
            reply.SetBootfileNameOption(scope.BootFileName);

        if (
            IncludesOrNoPreference(
                requestedTagsPresent,
                requestedTags,
                Dhcpv4OptionTagEnum.TftpServerName
            )
        )
            reply.SetTftpServerNameOption(nextServerAddress.ToString());
    }

    /// <summary>
    ///     This method reports whether a given option tag should be included: always, when the
    ///     client sent no Parameter Request List at all, or only when the client's own list names
    ///     it.
    /// </summary>
    private static bool IncludesOrNoPreference(
        bool requestedTagsPresent,
        IReadOnlyList<byte> requestedTags,
        Dhcpv4OptionTagEnum tag
    ) => !requestedTagsPresent || requestedTags.Contains((byte)tag);

    /// <summary>
    ///     This method computes the dotted-quad subnet mask for a CIDR prefix length -- for
    ///     example, <c>24</c> becomes <c>255.255.255.0</c>.
    /// </summary>
    private static IPAddress ComputeSubnetMask(int prefixLength)
    {
        uint mask = prefixLength == 0 ? 0 : ~(uint.MaxValue >> prefixLength);
        byte[] bytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, mask);
        return new IPAddress(bytes);
    }

    /// <summary>
    ///     This method attempts to parse every address in a list, failing the whole conversion if
    ///     any single one is invalid -- a scope's own <see cref="DhcpScopeEntity.GatewayAddresses" />/
    ///     <see cref="DhcpScopeEntity.DnsServerAddresses" /> are expected to already be validated
    ///     at write time, so a parse failure here indicates stored data this engine should not
    ///     silently send a truncated version of.
    /// </summary>
    private static bool TryParseAll(
        IReadOnlyList<string> addresses,
        out IReadOnlyList<IPAddress> parsed
    )
    {
        List<IPAddress> result = new(addresses.Count);

        foreach (string address in addresses)
        {
            if (!IPAddress.TryParse(address, out IPAddress parsedAddress))
            {
                parsed = null;
                return false;
            }

            result.Add(parsedAddress);
        }

        parsed = result;
        return true;
    }
}
