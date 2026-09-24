using System.Net;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This interface owns every real lease-allocation decision the DHCPv4 engine makes: handing
///     out a tentative address for a DISCOVER, committing a specific address for a REQUEST,
///     quarantining an address a client DECLINEd, and freeing an address a client RELEASEd.
///     Static reservations, lease-time bookkeping, and pool scanning are all implemented here,
///     against the storage-agnostic <c>Ninlil.Dhcp.Data.Abstractions</c> repositories -- nothing
///     above this layer (<see cref="Dhcpv4RequestHandler" />) knows how an address is actually
///     chosen.
/// </summary>
public interface IDhcpv4LeaseAllocationService
{
    /// <summary>
    ///     This method resolves and tentatively reserves an address to offer a client that just
    ///     sent a DISCOVER, per RFC 2131 Section 4.3.1: a static reservation, if the client has
    ///     one; otherwise the client's own already-held address (of any lifecycle state), if it
    ///     still falls within the scope's current range; otherwise the client's own requested
    ///     address (RFC 2132 Section 9.1's Requested IP Address option), if free and in range;
    ///     otherwise the first free address in the scope's pool.
    /// </summary>
    /// <param name="scope">The scope to allocate from.</param>
    /// <param name="request">The client's own DISCOVER message.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The lease record now tentatively offered (<see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Offered" />),
    ///     or <see langword="null" /> if no address is currently available in this scope at all.
    /// </returns>
    Task<LeaseEntity> OfferAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method commits a specific address to a client that sent a REQUEST, per RFC 2131
    ///     Section 4.3.2, moving the corresponding lease to
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" /> with the given lease
    ///     duration. The caller (<see cref="Dhcpv4RequestHandler" />) is responsible for deciding
    ///     which address to confirm and how long the lease should run for, per whichever of the
    ///     three REQUEST scenarios RFC 2131 describes (selecting/init-reboot/renewing) applies --
    ///     this method only validates that the address is genuinely available to this client and,
    ///     if so, commits it.
    /// </summary>
    /// <param name="scope">The scope <paramref name="address" /> belongs to.</param>
    /// <param name="request">The client's own REQUEST message.</param>
    /// <param name="address">The address to confirm.</param>
    /// <param name="leaseDuration">
    ///     The already-negotiated lease duration to grant (see
    ///     <see cref="Dhcpv4LeaseTimeNegotiator" />).
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The now-active lease record, or <see langword="null" /> if <paramref name="address" />
    ///     is not available to this client (out of the scope's range, reserved to a different
    ///     client, or currently held by a different client) -- the caller answers with a
    ///     DHCPNAK in that case.
    /// </returns>
    Task<LeaseEntity> ConfirmAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress address,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method quarantines an address a client reported as already in use by something
    ///     else on the network (RFC 2131 Section 4.3.3's DHCPDECLINE) for
    ///     <see cref="DhcpServerOptions.DeclinedAddressQuarantineDuration" />, for every client --
    ///     not only the one that declined it.
    /// </summary>
    /// <param name="scope">The scope <paramref name="declinedAddress" /> belongs to.</param>
    /// <param name="request">The client's own DECLINE message.</param>
    /// <param name="declinedAddress">The address the client reported as already in use.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task DeclineAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress declinedAddress,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method frees an address a client voluntarily gave back before its lease expired
    ///     (RFC 2131 Section 4.3.6's DHCPRELEASE), immediately returning it to the scope's own
    ///     free-address pool.
    /// </summary>
    /// <param name="scope">The scope <paramref name="releasedAddress" /> belongs to.</param>
    /// <param name="request">The client's own RELEASE message.</param>
    /// <param name="releasedAddress">The address the client is giving back.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task ReleaseAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress releasedAddress,
        CancellationToken cancellationToken = default
    );
}
