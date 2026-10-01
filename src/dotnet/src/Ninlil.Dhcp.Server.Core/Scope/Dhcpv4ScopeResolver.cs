using System.Net;
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
///         <b>Order of checks.</b> <see cref="Protocol.Dhcpv4.Dhcpv4UdpListener" /> binds one
///         wildcard socket and does not know which interface a datagram arrived on (see
///         <see cref="DhcpServerOptions.ServerIdentifierAddress" />), so an unrelayed request gives
///         no direct hint of its subnet. The resolver tries, in order:
///         <list type="number">
///             <item>an enabled reservation for this client, in any scope;</item>
///             <item>
///                 any lease row for this client (in any state), in any scope, which covers
///                 renewing, rebinding and rebooting clients;
///             </item>
///             <item>
///                 for a relayed request, the scope whose subnet contains the relay agent address
///                 (<c>giaddr</c>, RFC 2131 Section 4.1), since the relay sits on the client's
///                 subnet;
///             </item>
///             <item>
///                 for a request with a client address (<c>ciaddr</c>: renewing, rebinding or
///                 INFORM), the scope whose subnet contains it;
///             </item>
///             <item>
///                 otherwise, the only enabled scope if there is exactly one, also when steps 3
///                 or 4 had an address that matched no scope.
///             </item>
///         </list>
///         The first two steps come before <c>giaddr</c>, so a client with an old lease or a
///         reservation in one scope is served from that scope even when it is relayed from
///         another subnet. The single-scope fallback suits the usual home deployment with one LAN;
///         with several scopes, an unrelayed new client that has no <c>ciaddr</c> gets no scope.
///     </para>
/// </summary>
/// <param name="scopeRepository">The repository scopes are read through.</param>
/// <param name="reservationRepository">The repository reservations are read through.</param>
/// <param name="leaseRepository">The repository leases are read through.</param>
public sealed class Dhcpv4ScopeResolver(
    IDhcpScopeRepository scopeRepository,
    IDhcpReservationRepository reservationRepository,
    ILeaseRepository leaseRepository
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

        // 2. An existing lease for this client, wherever it is.
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

        // 3. A relayed request: the scope whose subnet contains the relay agent's address.
        if (!request.RelayAgentIpAddress.Equals(IPAddress.Any))
        {
            DhcpScopeEntity relayScope = FindScopeContaining(scopes, request.RelayAgentIpAddress);
            if (relayScope is not null)
                return relayScope;
        }
        // 4. A request with a usable client address already (renewing/rebinding/INFORM): the
        // scope whose subnet contains it.
        else if (!request.ClientIpAddress.Equals(IPAddress.Any))
        {
            DhcpScopeEntity clientScope = FindScopeContaining(scopes, request.ClientIpAddress);
            if (clientScope is not null)
                return clientScope;
        }

        // 5. Last resort: the only enabled scope, if there is exactly one. An address that
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
