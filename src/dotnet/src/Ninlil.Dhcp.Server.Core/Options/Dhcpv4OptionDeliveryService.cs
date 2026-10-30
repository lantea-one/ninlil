using System.Net;
using System.Net.Sockets;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This static class puts a scope's standard DHCPv4 options on a reply: the Server Identifier,
///     the IP Address Lease Time (for OFFER and ACK, not INFORM), Subnet Mask, Router and Domain
///     Name Server (RFC 2131 Section 4.3.1, Table 3), and the PXE boot fields. <see cref="Dhcpv4RequestHandler" />
///     calls it before <see cref="IDhcpv4OptionOverlayService" />.
///     <para>
///         RFC 2131 Section 3.5 lets a client list the parameters it wants in its Parameter Request
///         List (tag 55, RFC 2132 Section 9.8, where the server "MUST try to insert the requested
///         options in the order requested"), and this class answers with the ones it has values
///         for. Subnet Mask, Router, Domain Name Server and the Bootfile Name and TFTP Server Name
///         options are therefore sent only if listed, when the client sent a list; a client with
///         no list gets all of them.
///     </para>
/// </summary>
internal static class Dhcpv4OptionDeliveryService
{
    /// <summary>
    ///     This method applies the scope's standard options to a reply.
    /// </summary>
    /// <param name="reply">The reply to add options to.</param>
    /// <param name="scope">The scope whose values are used.</param>
    /// <param name="request">
    ///     The request, read for its Parameter Request List.
    /// </param>
    /// <param name="serverIdentifier">The server's Server Identifier address.</param>
    /// <param name="leaseDuration">
    ///     The lease duration for the IP Address Lease Time option, or <see langword="null" /> to
    ///     leave it out, as for a DHCPINFORM reply (RFC 2131 Section 4.3.5), where the client
    ///     already has an address.
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
            && TryParseIPv4(scope.GatewayAddresses, out IReadOnlyList<IPAddress> gateways)
        )
            reply.SetRouters(gateways);

        if (
            scope.DnsServerAddresses.Count > 0
            && IncludesOrNoPreference(
                requestedTagsPresent,
                requestedTags,
                Dhcpv4OptionTagEnum.DomainNameServer
            )
            && TryParseIPv4(scope.DnsServerAddresses, out IReadOnlyList<IPAddress> dnsServers)
        )
            reply.SetDomainNameServers(dnsServers);

        ApplyPxeBootOptions(reply, scope, serverIdentifier, requestedTagsPresent, requestedTags);
    }

    /// <summary>
    ///     This method applies the scope's PXE boot settings, if <see cref="DhcpScopeEntity.BootFileName" />
    ///     is set: always the RFC 2131 §2 "file" and "siaddr" header fields (header fields are not
    ///     subject to the Parameter Request List), and the RFC 2132 §9.5 Bootfile Name and §9.4
    ///     TFTP Server Name options under the Parameter Request List rule. The TFTP Server Name
    ///     option carries the address as text.
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
    ///     This method reports whether an option tag should be included: always when the client
    ///     sent no Parameter Request List, otherwise only when the list names it.
    /// </summary>
    private static bool IncludesOrNoPreference(
        bool requestedTagsPresent,
        IReadOnlyList<byte> requestedTags,
        Dhcpv4OptionTagEnum tag
    ) => !requestedTagsPresent || requestedTags.Contains((byte)tag);

    /// <summary>
    ///     This method computes the subnet mask for a prefix length (<c>24</c> gives
    ///     <c>255.255.255.0</c>).
    /// </summary>
    private static IPAddress ComputeSubnetMask(int prefixLength)
    {
        uint mask = prefixLength == 0 ? 0 : ~(uint.MaxValue >> prefixLength);
        byte[] bytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, mask);
        return new IPAddress(bytes);
    }

    /// <summary>
    ///     This method parses a list like <see cref="TryParseAll" /> and keeps the IPv4 entries
    ///     only, failing when none is left. The writers (<c>SetRouters</c>,
    ///     <c>SetDomainNameServers</c>) throw for an IPv6 address, which would lose the whole
    ///     reply, so a wrong-family entry that the API would refuse but that is stored anyway is
    ///     left out here.
    /// </summary>
    private static bool TryParseIPv4(
        IReadOnlyList<string> addresses,
        out IReadOnlyList<IPAddress> ipv4Addresses
    )
    {
        ipv4Addresses = null;

        if (!TryParseAll(addresses, out IReadOnlyList<IPAddress> parsed))
            return false;

        List<IPAddress> result =
        [
            .. parsed.Where(address => address.AddressFamily == AddressFamily.InterNetwork),
        ];

        if (result.Count == 0)
            return false;

        ipv4Addresses = result;
        return true;
    }

    /// <summary>
    ///     This method parses every address in a list, failing if any one is invalid, in which
    ///     case the option is left out. <see cref="DhcpScopeEntity.GatewayAddresses" /> and
    ///     <see cref="DhcpScopeEntity.DnsServerAddresses" /> are validated when written, so a bad
    ///     entry means bad stored data, which is not sent in part. The address family is not
    ///     checked here; <see cref="TryParseIPv4" /> does that for the callers that need IPv4.
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
