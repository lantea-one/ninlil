using System.Net;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This interface owns every real lease-allocation decision the DHCPv6 engine makes, for
///     both RFC 8415 stateful address assignment (IA_NA) and Prefix Delegation (IA_PD) -- the
///     DHCPv6 analogue of <see cref="IDhcpv4LeaseAllocationService" />. A DECLINE has no Prefix
///     Delegation analogue at all (RFC 8415 defines it only for addresses, since duplicate address
///     detection is what a client's own DECLINE reports), so there is no
///     <c>DeclinePrefixAsync</c>; <see cref="ReleaseAsync" /> is shared, since giving back a lease
///     row -- whether it holds an address or a delegated prefix -- is the same operation either
///     way.
///     <para>
///         <b>Design note -- only the first IA_NA/IA_PD in a message is ever processed</b>: RFC
///         8415 allows a client to carry more than one IA_NA/IA_PD in a single message, but
///         <c>LeaseConfiguration</c>'s own unique index (inherited unchanged from DHCPv4) still
///         only permits one lease row per <c>(ScopeName, ClientIdentifier)</c> -- widening that key
///         to also include an IAID would be a real, if plausible, future schema change, not
///         attempted in this sub-phase. <see cref="Dhcpv6RequestHandler" /> therefore only ever
///         looks at the first IA_NA and the first IA_PD a message carries, a deliberate, honest
///         scope cut rather than a silent limitation -- entirely sufficient for the overwhelming
///         majority of real clients (a home router or end-host client requesting exactly one
///         address and, if any, one delegated prefix per exchange).
///     </para>
/// </summary>
public interface IDhcpv6LeaseAllocationService
{
    /// <summary>
    ///     This method resolves and tentatively reserves an address to offer a client that just
    ///     sent a SOLICIT (without Rapid Commit), per RFC 8415 Section 18.3.9: a static
    ///     reservation, if the client has one; otherwise the client's own already-held address (of
    ///     any lifecycle state), if it still falls within the scope's current range; otherwise the
    ///     client's own hinted address (an <c>IAADDR</c> already present in the client's own
    ///     IA_NA), if free and in range; otherwise the first free address found scanning the
    ///     scope's pool.
    /// </summary>
    /// <param name="scope">The scope to allocate from.</param>
    /// <param name="clientKey">The client's own formatted DUID.</param>
    /// <param name="association">The specific IA_NA being offered an address.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The lease record now tentatively offered, or <see langword="null" /> if no address is
    ///     currently available in this scope at all.
    /// </returns>
    Task<LeaseEntity> OfferAddressAsync(
        DhcpScopeEntity scope,
        string clientKey,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses association,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method commits a specific address to a client, per RFC 8415 Section 18.3.2's
    ///     (REQUEST) or 18.3.4's (RENEW/REBIND) own processing, moving the corresponding lease to
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" /> with the given lifetimes.
    /// </summary>
    /// <param name="scope">The scope <paramref name="address" /> belongs to.</param>
    /// <param name="clientKey">The client's own formatted DUID.</param>
    /// <param name="address">The address to confirm.</param>
    /// <param name="leaseTimes">The already-negotiated lifetimes to grant.</param>
    /// <param name="hostName">The host name to record for this lease, if the client presented one.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The now-active lease record, or <see langword="null" /> if <paramref name="address" />
    ///     is not available to this client -- the caller answers with a Status Code option in that
    ///     case, DHCPv6 having no DHCPv4-style NAK message type of its own.
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
    ///     This method quarantines an address a client reported as already in use by something
    ///     else on the network (RFC 8415 Section 18.2.7/18.3.7's DECLINE) for
    ///     <see cref="DhcpServerOptions.DeclinedAddressQuarantineDuration" />, for every client --
    ///     the same shared quarantine window and representation (see
    ///     <c>Dhcpv4LeaseAllocationService</c>'s own doc comment) DHCPv4 already uses.
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
    ///     This method resolves and tentatively reserves a prefix to offer a client that just sent
    ///     a SOLICIT (without Rapid Commit), the Prefix Delegation analogue of
    ///     <see cref="OfferAddressAsync" />: the client's own already-held prefix, if it still
    ///     falls within the scope's current pool; otherwise the client's own hinted prefix, if
    ///     free; otherwise the first free block-aligned prefix found scanning the scope's pool. No
    ///     static-reservation lookup -- see this interface's own doc comment for why Prefix
    ///     Delegation reservations are a deliberate, deferred scope cut.
    /// </summary>
    /// <param name="scope">The scope to allocate from.</param>
    /// <param name="clientKey">The client's own formatted DUID.</param>
    /// <param name="association">The specific IA_PD being offered a prefix.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The lease record now tentatively offered, or <see langword="null" /> if no prefix is
    ///     currently available in this scope at all.
    /// </returns>
    Task<LeaseEntity> OfferPrefixAsync(
        DhcpScopeEntity scope,
        string clientKey,
        Dhcpv6IdentityAssociationForPrefixDelegation association,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     This method commits a specific delegated prefix to a client, the Prefix Delegation
    ///     analogue of <see cref="ConfirmAddressAsync" />.
    /// </summary>
    /// <param name="scope">The scope <paramref name="prefix" /> belongs to.</param>
    /// <param name="clientKey">The client's own formatted DUID.</param>
    /// <param name="prefix">The prefix's own base address to confirm.</param>
    /// <param name="prefixLength">The prefix's own length, in bits.</param>
    /// <param name="leaseTimes">The already-negotiated lifetimes to grant.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The now-active lease record, or <see langword="null" /> if the prefix is not available
    ///     to this client.
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
    ///     This method frees a lease a client voluntarily gave back before it expired (RFC 8415
    ///     Section 18.2.8/18.3.6's RELEASE), immediately returning it to the scope's own free
    ///     pool -- shared between an address and a delegated-prefix lease, per this interface's own
    ///     doc comment.
    /// </summary>
    /// <param name="scope">The scope the released lease belongs to.</param>
    /// <param name="clientKey">The client's own formatted DUID.</param>
    /// <param name="value">The address, or delegated prefix's own base address, being released.</param>
    /// <param name="prefixLength">
    ///     The prefix length being released, for a Prefix Delegation lease, or
    ///     <see langword="null" /> for an ordinary address lease.
    /// </param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> if a matching lease was found and released;
    ///     <see langword="false" /> if this client holds no matching lease in this scope at all
    ///     (the caller reports <c>NoBinding</c> in that case, per RFC 8415 Section 18.3.6).
    /// </returns>
    Task<bool> ReleaseAsync(
        DhcpScopeEntity scope,
        string clientKey,
        IPAddress value,
        int? prefixLength,
        CancellationToken cancellationToken = default
    );
}
