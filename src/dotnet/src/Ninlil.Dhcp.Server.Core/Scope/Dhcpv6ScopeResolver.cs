using System.Net;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Allocation;
using Ninlil.Dhcp.Server.Core.Network;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Scope;

/// <summary>
///     This class implements <see cref="IDhcpv6ScopeResolver" /> on the scope, reservation and
///     lease repositories. Only enabled IPv6 scopes of the requested kind are considered, and a
///     request without a Client Identifier gets no scope.
///     <para>
///         <b>Order of checks</b>, like <see cref="Dhcpv4ScopeResolver" />:
///         <list type="number">
///             <item>
///                 an enabled reservation for this client, for address-assignment scopes only
///                 (reservations hold an address, not a delegated prefix);
///             </item>
///             <item>any lease row for this client in a scope of the requested kind;</item>
///             <item>
///                 for a relayed request, the scope whose subnet contains the RELAY-FORW
///                 <c>link-address</c>, which <c>Dhcpv6RequestHandler.HandleRelayMessageAsync</c>
///                 copies to <see cref="Dhcpv6Message.RelayLinkAddress" /> (the counterpart of
///                 <c>giaddr</c>);
///             </item>
///             <item>
///                 otherwise, the only enabled scope of that kind if there is exactly one, also
///                 when the link address matched no scope.
///             </item>
///         </list>
///         An unrelayed request has no subnet hint: a DHCPv6 client sends from its link-local
///         address, which says nothing about the global subnet a scope serves (unlike DHCPv4's
///         <c>ciaddr</c>).
///     </para>
/// </summary>
/// <param name="scopeRepository">The repository scopes are read through.</param>
/// <param name="reservationRepository">The repository reservations are read through.</param>
/// <param name="leaseRepository">The repository leases are read through.</param>
public sealed class Dhcpv6ScopeResolver(
    IDhcpScopeRepository scopeRepository,
    IDhcpReservationRepository reservationRepository,
    ILeaseRepository leaseRepository
) : IDhcpv6ScopeResolver
{
    /// <inheritdoc />
    public async Task<DhcpScopeEntity> ResolveAsync(
        Dhcpv6Message request,
        DhcpScopeKindEnum kind,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<DhcpScopeEntity> allScopes = await scopeRepository.ListAsync(
            scope => scope.ProtocolVersion == InternetProtocolVersionEnum.IPv6 && !scope.IsDisabled,
            cancellationToken
        );
        List<DhcpScopeEntity> scopes = [.. allScopes.Where(scope => scope.Kind == kind)];

        if (scopes.Count == 0)
            return null;

        string clientKey = Dhcpv6ClientIdentifier.Format(request);

        if (clientKey is null)
            return null;

        // 1. A reservation for this client in any scope (address-assignment scopes only).
        if (kind == DhcpScopeKindEnum.AddressAssignment)
        {
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

        // 3. A relayed request: the scope whose subnet contains the relay's link address.
        if (request.RelayLinkAddress is not null)
        {
            DhcpScopeEntity relayScope = FindScopeContaining(scopes, request.RelayLinkAddress);
            if (relayScope is not null)
                return relayScope;
        }

        // 4. Last resort: the only enabled scope of this kind, if there is exactly one. A link
        // address that matched no scope is treated like none, as in Dhcpv4ScopeResolver.
        return scopes.Count == 1 ? scopes[0] : null;
    }

    /// <summary>
    ///     This method returns the first scope whose subnet contains an address, like the helper
    ///     of the same name in <see cref="Dhcpv4ScopeResolver" />.
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
                && Dhcpv6NetworkHelper.AreInSameNetwork(
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
