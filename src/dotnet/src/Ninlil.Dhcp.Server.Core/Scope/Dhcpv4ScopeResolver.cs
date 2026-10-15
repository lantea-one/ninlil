using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Allocation;
using Ninlil.Dhcp.Server.Core.Network;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Scope;

/// <summary>
///     This class implements <see cref="IDhcpv4ScopeResolver" /> on the scope, reservation and
///     lease repositories. Only enabled IPv4 scopes are considered.
///     <para>
///         <b>Relayed requests.</b> A request with a relay agent address (<c>giaddr</c>) is
///         served from the scope whose subnet contains that address, and from no other: the relay
///         sits on the client's subnet (RFC 2131 Section 4.1, Section 4.3.1), so an address from
///         another scope would not work on the client's link. When no enabled scope contains the
///         relay address the resolver returns <see langword="null" /> (no reply) and logs at
///         Debug. Reservations and lease rows in other scopes are not consulted, so a client
///         that moved to another relayed subnet gets an address there.
///     </para>
///     <para>
///         <b>Unrelayed requests.</b> <see cref="Protocol.Dhcpv4.Dhcpv4UdpListener" /> binds one
///         wildcard socket and does not know which interface a datagram arrived on (see
///         <see cref="DhcpServerOptions.ServerIdentifierAddress" />), so the resolver tries, in
///         order:
///         <list type="number">
///             <item>
///                 an enabled reservation for this client, in any scope, so a reservation added
///                 later takes effect;
///             </item>
///             <item>
///                 for a request with a client address (<c>ciaddr</c>: renewing, rebinding or
///                 INFORM), the scope whose subnet contains it, which is direct evidence of the
///                 client's link;
///             </item>
///             <item>
///                 any lease row for this client (in any state), in any scope, which covers
///                 rebooting clients;
///             </item>
///             <item>
///                 otherwise, the only enabled scope if there is exactly one, also when the
///                 <c>ciaddr</c> matched no scope.
///             </item>
///         </list>
///         The single-scope fallback suits the usual home deployment with one LAN; with several
///         scopes, an unrelayed new client that has no <c>ciaddr</c> gets no scope.
///     </para>
/// </summary>
/// <param name="scopeRepository">The repository scopes are read through.</param>
/// <param name="reservationRepository">The repository reservations are read through.</param>
/// <param name="leaseRepository">The repository leases are read through.</param>
/// <param name="logger">The logger a relay address that matches no scope is reported to.</param>
public sealed class Dhcpv4ScopeResolver(
    IDhcpScopeRepository scopeRepository,
    IDhcpReservationRepository reservationRepository,
    ILeaseRepository leaseRepository,
    ILogger<Dhcpv4ScopeResolver> logger
) : IDhcpv4ScopeResolver
{
    /// <inheritdoc />
    public async Task<DhcpScopeEntity> ResolveAsync(
        Dhcpv4Message request,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<DhcpScopeEntity> scopes = await scopeRepository.ListAsync(
            scope => scope.ProtocolVersion == InternetProtocolVersionEnum.IPv4 && !scope.IsDisabled,
            cancellationToken
        );

        if (scopes.Count == 0)
            return null;

        // A relayed request belongs to the relay's subnet and to no other scope (RFC 2131
        // Section 4.1, Section 4.3.1); an address from elsewhere would not work on its link.
        if (!request.RelayAgentIpAddress.Equals(IPAddress.Any))
        {
            DhcpScopeEntity relayScope = FindScopeContaining(scopes, request.RelayAgentIpAddress);
            if (relayScope is null)
                logger.LogDebug(
                    "DHCPv4 request relayed from {RelayAddress}, which no enabled scope covers; no reply",
                    request.RelayAgentIpAddress
                );

            return relayScope;
        }

        string clientKey = Dhcpv4ClientIdentifier.Format(request);

        // 1. A static reservation for this client, wherever it is.
        foreach (DhcpScopeEntity scope in scopes)
        {
            DhcpReservationEntity reservation =
                await reservationRepository.FindByClientIdentifierAsync(
                    scope.Id,
                    clientKey,
                    cancellationToken
                );

            if (reservation is { IsDisabled: false })
                return scope;
        }

        // 2. A request with a usable client address already (renewing/rebinding/INFORM): the
        // scope whose subnet contains it.
        if (!request.ClientIpAddress.Equals(IPAddress.Any))
        {
            DhcpScopeEntity clientScope = FindScopeContaining(scopes, request.ClientIpAddress);
            if (clientScope is not null)
                return clientScope;
        }

        // 3. An existing lease for this client, wherever it is.
        foreach (DhcpScopeEntity scope in scopes)
        {
            LeaseEntity lease = await leaseRepository.FindByClientIdentifierAsync(
                scope.Name,
                clientKey,
                cancellationToken
            );

            if (lease is not null)
                return scope;
        }

        // 4. Last resort: the only enabled scope, if there is exactly one. A client address that
        // matched no scope is treated like no address.
        return scopes.Count == 1 ? scopes[0] : null;
    }

    /// <summary>
    ///     This method returns the first scope whose subnet contains an address. The scope's
    ///     <see cref="DhcpScopeEntity.RangeStartAddress" /> stands in for the network, since
    ///     <see cref="Dhcpv4NetworkHelper.AreInSameNetwork" /> only compares masked addresses.
    /// </summary>
    private static DhcpScopeEntity FindScopeContaining(
        IReadOnlyList<DhcpScopeEntity> scopes,
        IPAddress address
    )
    {
        foreach (DhcpScopeEntity scope in scopes)
        {
            if (
                IPAddress.TryParse(scope.RangeStartAddress, out IPAddress scopeAddress)
                && Dhcpv4NetworkHelper.AreInSameNetwork(
                    scopeAddress,
                    address,
                    scope.SubnetPrefixLength
                )
            )
                return scope;
        }

        return null;
    }
}
