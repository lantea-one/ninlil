using System.Net;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This static class applies a scope's own standard DHCPv6 configuration options onto an
///     outgoing reply message -- the DHCPv6 analogue of <see cref="Dhcpv4OptionDeliveryService" />,
///     narrower in scope: RFC 8415 Section 18.3 requires every reply to carry a Server Identifier
///     (this server's own DUID) and a Client Identifier (echoing the client's own DUID back), and
///     this project additionally hands out RFC 3646 Section 3's DNS Recursive Name Server option
///     from <see cref="DhcpScopeEntity.DnsServerAddresses" /> -- the same list a DHCPv4 scope's own
///     Domain Name Server option already draws from, genuinely shared between both protocols since
///     it is nothing more than "which DNS servers to hand a client," independent of which IP
///     version carried the request.
///     <para>
///         RFC 3646's own Domain Search List option, and RFC 8415's Preference option, are
///         deliberately not delivered yet -- a documented, deferred refinement, not an oversight:
///         neither has a configured source on <see cref="DhcpScopeEntity" /> today (a Domain
///         Search List would need its own new scope field, the same way <c>DnsServerAddresses</c>
///         already has one; Preference only matters once more than one DHCPv6 server can plausibly
///         answer the same client, which this project's own single-server homelab deployment
///         target does not need).
///     </para>
/// </summary>
internal static class Dhcpv6OptionDeliveryService
{
    /// <summary>
    ///     This method applies a scope's own standard option values, plus the two identifier
    ///     options RFC 8415 requires unconditionally, onto a reply message.
    /// </summary>
    /// <param name="reply">The reply message to apply options onto.</param>
    /// <param name="scope">
    ///     The scope whose own configured option values should be applied, or
    ///     <see langword="null" /> when no scope could be resolved for this exchange at all (an
    ///     Information-Request with no matching scope, for example) -- the two identifier options
    ///     are still applied in that case, simply with no DNS servers to add alongside them.
    /// </param>
    /// <param name="request">The original request, consulted for its own Client Identifier.</param>
    /// <param name="serverDuid">This server's own DUID.</param>
    public static void Apply(
        Dhcpv6Message reply,
        DhcpScopeEntity scope,
        Dhcpv6Message request,
        byte[] serverDuid
    )
    {
        reply.SetServerIdentifier(serverDuid);

        if (request.TryGetClientIdentifier(out byte[] clientDuid))
            reply.SetClientIdentifier(clientDuid);

        bool requestedCodesPresent = request.TryGetOptionRequest(
            out IReadOnlyList<ushort> requestedCodes
        );

        if (
            scope is { DnsServerAddresses.Count: > 0 }
            && IncludesOrNoPreference(
                requestedCodesPresent,
                requestedCodes,
                Common.Enum.Dhcpv6OptionCodeEnum.DnsRecursiveNameServer
            )
            && TryParseAll(scope.DnsServerAddresses, out IReadOnlyList<IPAddress> dnsServers)
        )
            reply.SetDnsServers(dnsServers);
    }

    /// <summary>
    ///     This method reports whether a given option code should be included: always, when the
    ///     client sent no Option Request Option at all, or only when the client's own list names
    ///     it -- the DHCPv6 analogue of <see cref="Dhcpv4OptionDeliveryService" />'s own
    ///     equivalent helper.
    /// </summary>
    private static bool IncludesOrNoPreference(
        bool requestedCodesPresent,
        IReadOnlyList<ushort> requestedCodes,
        Common.Enum.Dhcpv6OptionCodeEnum code
    ) => !requestedCodesPresent || requestedCodes.Contains((ushort)code);

    /// <summary>
    ///     This method attempts to parse every address in a list, failing the whole conversion if
    ///     any single one is invalid -- mirrors <see cref="Dhcpv4OptionDeliveryService" />'s own
    ///     equivalent helper exactly.
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
