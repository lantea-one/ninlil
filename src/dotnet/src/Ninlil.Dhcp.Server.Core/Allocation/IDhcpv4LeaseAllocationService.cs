using System.Net;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This interface makes the DHCPv4 lease decisions: offering an address for a DISCOVER,
///     committing one for a REQUEST, quarantining a DECLINEd address and freeing a RELEASEd one.
///     Reservations, lease bookkeeping and the pool scan live behind it, on the
///     <c>Ninlil.Dhcp.Data.Abstractions</c> repositories; <see cref="Dhcpv4RequestHandler" />
///     calls it without knowing how addresses are chosen.
/// </summary>
public interface IDhcpv4LeaseAllocationService
{
    /// <summary>
    ///     This method picks an address for a DISCOVER (RFC 2131 Section 4.3.1) and holds it for
    ///     the client as an offer. In order: the address on the client's existing lease row,
    ///     when that row is not <c>Expired</c> and the address is still in range; the client's
    ///     enabled reservation; the address the client asked for (Requested IP Address, RFC 2132
    ///     Section 9.1) when it is free, in range and not reserved for someone else; otherwise
    ///     the first free address in the pool.
    /// </summary>
    /// <param name="scope">The scope to allocate from.</param>
    /// <param name="request">The client's DISCOVER message.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The lease row, now <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Offered" />, or
    ///     <see langword="null" /> if no address is available in the scope.
    /// </returns>
    Task<LeaseEntity> OfferAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method commits an address to a client that sent a REQUEST (RFC 2131 Section
    ///     4.3.2), making its lease <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" /> for
    ///     the given duration and publishing a granted event. <see cref="Dhcpv4RequestHandler" />
    ///     decides which address and duration apply to the REQUEST (selecting, init-reboot or
    ///     renewing); this method only checks that the address is available to the client.
    /// </summary>
    /// <param name="scope">The scope <paramref name="address" /> belongs to.</param>
    /// <param name="request">The client's REQUEST message.</param>
    /// <param name="address">The address to confirm.</param>
    /// <param name="leaseDuration">
    ///     The lease duration to grant, already negotiated (see
    ///     <see cref="Dhcpv4LeaseTimeNegotiator" />).
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The active lease, or <see langword="null" /> if <paramref name="address" /> is not
    ///     available to this client (outside the range, reserved for another client, held by
    ///     another client, or not the client's own reserved address); the caller then sends a
    ///     DHCPNAK.
    /// </returns>
    Task<LeaseEntity> ConfirmAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress address,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method withholds an address a client reported as already in use (RFC 2131
    ///     Section 4.3.3, DHCPDECLINE) for <see cref="DhcpServerOptions.DeclinedAddressQuarantineDuration" />,
    ///     by marking the lease row for that address <c>Expired</c> with a future expiry.
    /// </summary>
    /// <param name="scope">The scope <paramref name="declinedAddress" /> belongs to.</param>
    /// <param name="request">The client's DECLINE message.</param>
    /// <param name="declinedAddress">The address the client reported as already in use.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task DeclineAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress declinedAddress,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method frees an address a client gave back before its lease expired (RFC 2131
    ///     Section 4.3.6, DHCPRELEASE), so it can be handed out again at once, and publishes a
    ///     released event. A RELEASE that does not match the client's lease is ignored.
    /// </summary>
    /// <param name="scope">The scope <paramref name="releasedAddress" /> belongs to.</param>
    /// <param name="request">The client's RELEASE message.</param>
    /// <param name="releasedAddress">The address the client is giving back.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task ReleaseAsync(
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        IPAddress releasedAddress,
        CancellationToken cancellationToken = default
    );
}
