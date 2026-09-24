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
///     This class implements <see cref="IDhcpv6ScopeResolver" /> against the real
///     <see cref="IDhcpScopeRepository" />/<see cref="IDhcpReservationRepository" />/
///     <see cref="ILeaseRepository" /> repositories.
///     <para>
///         <b>Resolution order</b>, closely mirroring <see cref="Dhcpv4ScopeResolver" />'s own:
///         this method tries, in order: (1) a static reservation for this client (
///         <see cref="DhcpScopeKindEnum.AddressAssignment" /> only -- see
///         <see cref="Data.Abstractions.Entity.DhcpReservationEntity" />'s own lack of a
///         delegated-prefix shape, a deliberate, documented scope cut); (2) an existing lease for
///         this client, in a scope of the requested kind; (3) for a request that arrived wrapped in
///         a relay agent's own RELAY-FORW envelope, the scope whose own subnet contains that
///         envelope's own <c>link-address</c> (threaded onto the unwrapped inner message as
///         <see cref="Dhcpv6Message.RelayLinkAddress" /> by
///         <c>Dhcpv6RequestHandler.HandleRelayMessageAsync</c>) -- the DHCPv6 analogue of
///         <see cref="Dhcpv4ScopeResolver" />'s own relayed-request <c>giaddr</c> step; (4) as a
///         last resort, the single enabled scope of the requested protocol version and kind, if
///         there is exactly one -- the same "single-scope homelab deployment target" fallback
///         <see cref="Dhcpv4ScopeResolver" /> itself ends with. A non-relayed request has no
///         equivalent to step (3) at all: a DHCPv6 client's own UDP source address is always its
///         link-local address regardless of which real, globally-routed subnet a stateful scope
///         actually serves, so it carries no usable containment signal on its own (unlike DHCPv4's
///         own <c>ciaddr</c>, which is a real address drawn from the served subnet itself) --
///         <see cref="Dhcpv6Message.RelayLinkAddress" /> being unset is exactly how this method
///         tells a non-relayed request apart from one it can usefully key off.
///     </para>
/// </summary>
/// <param name="scopeRepository">The repository this resolver reads scopes through.</param>
/// <param name="reservationRepository">The repository this resolver reads reservations through.</param>
/// <param name="leaseRepository">The repository this resolver reads leases through.</param>
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

        // 1. A static reservation for this client, wherever it is -- address-assignment scopes
        // only, per this class's own doc comment.
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

        // 3. A relayed request: the scope whose subnet contains the relay's own link address.
        if (request.RelayLinkAddress is not null)
        {
            DhcpScopeEntity relayScope = FindScopeContaining(scopes, request.RelayLinkAddress);
            if (relayScope is not null)
                return relayScope;
        }

        // 4. Last resort: the single enabled scope of this kind, if there is exactly one -- reached
        // both when no relay link address was present at all, and when one was present but matched
        // no known scope's own subnet (see Dhcpv4ScopeResolver's own matching fallback comment for
        // why a genuinely mismatched address is treated the same as no address information at all).
        return scopes.Count == 1 ? scopes[0] : null;
    }

    /// <summary>
    ///     This method finds the scope, among a candidate list, whose own subnet contains a given
    ///     address -- the DHCPv6 analogue of <see cref="Dhcpv4ScopeResolver" />'s own private
    ///     helper of the same name.
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
