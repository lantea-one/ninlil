using System.Net;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This interface makes the DHCPv6 lease decisions for RFC 8415 address assignment (IA_NA)
///     and prefix delegation (IA_PD), the counterpart of <see cref="IDhcpv4LeaseAllocationService" />.
///     There is no prefix DECLINE, since a DECLINE reports duplicate address detection, which only
///     applies to addresses. <see cref="ReleaseAsync" /> serves both kinds, since giving back an
///     address row and a prefix row is the same operation.
///     <para>
///         <b>One IA_NA and one IA_PD per message.</b> A client may send several, but the
///         <c>LeaseConfiguration</c> unique index allows one lease row per
///         <c>(ScopeName, ClientIdentifier)</c>; including the IAID in that key would need a
///         schema change. <see cref="Dhcpv6RequestHandler" /> therefore handles only the first
///         IA_NA and the first IA_PD of a message, which covers the usual client asking for one
///         address and at most one prefix.
///     </para>
/// </summary>
public interface IDhcpv6LeaseAllocationService
{
    /// <summary>
    ///     This method picks an address for a SOLICIT without Rapid Commit (an ADVERTISE, RFC 8415
    ///     Section 18.3.9) and holds it for the client as an offer. The order is described on
    ///     <see cref="Dhcpv6LeaseAllocationService" />.
    /// </summary>
    /// <param name="scope">The scope to allocate from.</param>
    /// <param name="clientKey">The client's formatted DUID.</param>
    /// <param name="association">The IA_NA being offered an address; its IAADDR is a hint.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The lease row, now offered, or <see langword="null" /> if no address is available.
    /// </returns>
    Task<LeaseEntity> OfferAddressAsync(
        DhcpScopeEntity scope,
        string clientKey,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses association,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method commits an address to a client for a REQUEST, RENEW or REBIND (RFC 8415
    ///     Sections 18.3.2 and 18.3.4) or a Rapid Commit SOLICIT, making its lease
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" /> with the given lifetimes.
    /// </summary>
    /// <param name="scope">The scope <paramref name="address" /> belongs to.</param>
    /// <param name="clientKey">The client's formatted DUID.</param>
    /// <param name="address">The address to confirm.</param>
    /// <param name="leaseTimes">The lifetimes to grant, already negotiated.</param>
    /// <param name="hostName">The host name to record for this lease, if the client presented one.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The active lease, or <see langword="null" /> if <paramref name="address" /> is not
    ///     available to this client; the caller then answers with a Status Code option, since
    ///     DHCPv6 has no NAK message.
    /// </returns>
    Task<LeaseEntity> ConfirmAddressAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress address,
        Dhcpv6LeaseTimes leaseTimes,
        string hostName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method withholds an address a client reported in a DECLINE as already in use, for
    ///     <see cref="DhcpServerOptions.DeclinedAddressQuarantineDuration" />, stored as in DHCPv4
    ///     (see <c>Dhcpv4LeaseAllocationService</c>).
    /// </summary>
    /// <param name="scope">The scope <paramref name="declinedAddress" /> belongs to.</param>
    /// <param name="declinedAddress">The address the client reported as already in use.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    Task DeclineAddressAsync(
        DhcpScopeEntity scope,
        IPAddress declinedAddress,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method picks a prefix for a SOLICIT without Rapid Commit and holds it as an offer,
    ///     the prefix counterpart of <see cref="OfferAddressAsync" />. Reservations are not
    ///     consulted, since a reservation holds an address, not a prefix (see
    ///     <see cref="Dhcpv6LeaseAllocationService" /> for the order).
    /// </summary>
    /// <param name="scope">The scope to allocate from.</param>
    /// <param name="clientKey">The client's formatted DUID.</param>
    /// <param name="association">The IA_PD being offered a prefix; its IAPREFIX is a hint.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The lease row, now offered, or <see langword="null" /> if no prefix is available.
    /// </returns>
    Task<LeaseEntity> OfferPrefixAsync(
        DhcpScopeEntity scope,
        string clientKey,
        Dhcpv6IdentityAssociationForPrefixDelegation association,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method commits a delegated prefix to a client, the prefix counterpart of
    ///     <see cref="ConfirmAddressAsync" />.
    /// </summary>
    /// <param name="scope">The scope <paramref name="prefix" /> belongs to.</param>
    /// <param name="clientKey">The client's formatted DUID.</param>
    /// <param name="prefix">The base address of the prefix to confirm.</param>
    /// <param name="prefixLength">The prefix length, in bits.</param>
    /// <param name="leaseTimes">The lifetimes to grant, already negotiated.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The active lease, or <see langword="null" /> if the prefix is not available to this
    ///     client.
    /// </returns>
    Task<LeaseEntity> ConfirmPrefixAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress prefix,
        int prefixLength,
        Dhcpv6LeaseTimes leaseTimes,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method frees an address or prefix lease the client gave back in a RELEASE, so it
    ///     can be handed out again at once.
    /// </summary>
    /// <param name="scope">The scope the released lease belongs to.</param>
    /// <param name="clientKey">The client's formatted DUID.</param>
    /// <param name="value">The address, or the base address of the prefix, being released.</param>
    /// <param name="prefixLength">
    ///     The prefix length for a prefix lease, or <see langword="null" /> for an address lease.
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> if a matching lease was found and released;
    ///     <see langword="false" /> if the client holds no matching lease in this scope (the
    ///     caller then reports <c>NoBinding</c>).
    /// </returns>
    Task<bool> ReleaseAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress value,
        int? prefixLength,
        CancellationToken cancellationToken = default
    );
}
