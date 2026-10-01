using System.Net;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This static class puts the standard DHCPv6 options on a reply, the narrower counterpart of
///     <see cref="Dhcpv4OptionDeliveryService" />: the Server Identifier (the server's DUID) and
///     the echoed Client Identifier that RFC 8415 Section 18.3 requires, and the RFC 3646 Section 3
///     DNS Recursive Name Server option from <see cref="DhcpScopeEntity.DnsServerAddresses" />
///     (the list the DHCPv4 Domain Name Server option also uses), sent only if the Option Request
///     Option lists it when the client sent one.
///     <para>
///         The Domain Search List and Preference options are not sent: <see cref="DhcpScopeEntity" />
///         has no field for a search list, and Preference only matters with several DHCPv6
///         servers. A search list can be sent through a tag rule.
///     </para>
/// </summary>
internal static class Dhcpv6OptionDeliveryService
{
    /// <summary>
    ///     This method applies the two identifier options and the scope's DNS servers to a reply.
    /// </summary>
    /// <param name="reply">The reply to add options to.</param>
    /// <param name="scope">
    ///     The scope whose values are used, or <see langword="null" /> when none was resolved
    ///     (an Information-Request with no matching scope, for example); the identifiers are then
    ///     still added, without DNS servers.
    /// </param>
    /// <param name="request">The request, read for its Client Identifier and Option Request Option.</param>
    /// <param name="serverDuid">The server's DUID.</param>
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
    ///     This method reports whether an option code should be included: always when the client
    ///     sent no Option Request Option, otherwise only when it lists the code.
    /// </summary>
    private static bool IncludesOrNoPreference(
        bool requestedCodesPresent,
        IReadOnlyList<ushort> requestedCodes,
        Common.Enum.Dhcpv6OptionCodeEnum code
    ) => !requestedCodesPresent || requestedCodes.Contains((ushort)code);

    /// <summary>
    ///     This method parses every address in a list, failing if any one is invalid, as in
    ///     <see cref="Dhcpv4OptionDeliveryService" />. IPv4 entries in the shared list parse but
    ///     make <c>SetDnsServers</c> throw.
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
