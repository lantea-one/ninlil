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
///     This class implements <see cref="IDhcpv4ScopeResolver" /> against the real
///     <see cref="IDhcpScopeRepository" />/<see cref="IDhcpReservationRepository" />/
///     <see cref="ILeaseRepository" /> repositories.
///     <para>
///         <b>Resolution order, and why</b>: this project's own
///         <see cref="Protocol.Dhcpv4.Dhcpv4UdpListener" /> binds one wildcard
///         (<c>INADDR_ANY</c>) socket and does not capture which network interface actually
///         received a given datagram (see <see cref="DhcpServerOptions.ServerIdentifierAddress" />'s
///         own doc comment for the same underlying limitation), so a non-relayed request carries
///         no direct signal at all about which of this server's own scopes it belongs to unless
///         the request itself already names a client this server recognizes. This method therefore
///         tries, in order: (1) a static reservation for this client, in any scope; (2) an
///         existing lease for this client, in any scope (covers a renewing/rebinding/init-reboot
///         client this server has already served before); (3) for a relayed request, the scope
///         whose own subnet contains the relay agent's address (RFC 2131 Section 4.1's
///         <c>giaddr</c> -- a relay agent, by definition, sits on the client's own subnet);
///         (4) for a request that already has a usable client address (<c>ciaddr</c> --
///         renewing/rebinding/INFORM), the scope whose own subnet contains it; (5) as a last
///         resort, the single enabled DHCPv4 scope, if there is exactly one -- reached both when
///         neither (3) nor (4) had any address to go on, and when one was present but matched no
///         known scope's own subnet, since a genuinely mismatched address is no less ambiguous,
///         for this project's own single-scope homelab deployment target, than no address
///         information being present in the first place. This is a deliberate, honest scope
///         decision for this project's own primary deployment target (a homelab or home router
///         typically serving one LAN), not a claim that a genuinely ambiguous, multi-scope request
///         can always be resolved correctly.
///     </para>
/// </summary>
/// <param name="scopeRepository">The repository this resolver reads scopes through.</param>
/// <param name="reservationRepository">The repository this resolver reads reservations through.</param>
/// <param name="leaseRepository">The repository this resolver reads leases through.</param>
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

        // 3. A relayed request: the scope whose subnet contains the relay agent's own address.
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

        // 5. Last resort: the single enabled scope, if there is exactly one -- reached both when
        // neither a relay agent address nor a usable client address was present at all, and when
        // one was present but matched no known scope's own subnet (a genuinely mismatched address
        // is no less ambiguous, for this project's own single-scope homelab deployment target,
        // than no address information being present in the first place).
        return scopes.Count == 1 ? scopes[0] : null;
    }

    /// <summary>
    ///     This method finds the scope, among a candidate list, whose own subnet contains a given
    ///     address -- using each scope's <see cref="DhcpScopeEntity.RangeStartAddress" /> as an
    ///     arbitrary member of that subnet, since <see cref="Dhcpv4NetworkHelper.AreInSameNetwork" />
    ///     only needs a masked comparison, not the true network address itself.
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
