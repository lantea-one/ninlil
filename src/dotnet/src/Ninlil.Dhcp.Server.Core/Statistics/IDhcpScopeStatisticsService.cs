using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Statistics;

/// <summary>
///     This interface computes a single DHCP scope's own real, current utilization -- how much of
///     its configured range is free right now versus already spoken for -- entirely from this
///     scope's own configuration plus its real, currently-recorded lease/reservation rows, never
///     from a cached or periodically-swept figure: every call re-reads both directly and recomputes
///     from scratch, so the answer is always as fresh as the underlying data.
/// </summary>
public interface IDhcpScopeStatisticsService
{
    /// <summary>
    ///     This method computes <paramref name="scope" />'s own current utilization.
    ///     <para>
    ///         <b>Total slots</b>: for an <see cref="Ninlil.Dhcp.Data.Abstractions.Enum.DhcpScopeKindEnum.AddressAssignment" />
    ///         scope, the inclusive count of individual addresses between <c>RangeStartAddress</c>
    ///         and <c>RangeEndAddress</c>. For an IPv6 <see cref="Ninlil.Dhcp.Data.Abstractions.Enum.DhcpScopeKindEnum.PrefixDelegation" />
    ///         scope, the count of block-aligned <c>DelegatedPrefixLength</c>-bit prefixes the
    ///         range holds -- mirroring exactly how <c>Network.Dhcpv6NetworkHelper.EnumeratePrefixes</c>
    ///         itself walks the same range when actually allocating one (including that method's
    ///         own "align the first candidate down to a block boundary" rule), just without that
    ///         method's own <c>MaximumPoolScanCandidates</c> cap -- this is a genuine total, not a
    ///         bounded scan.
    ///     </para>
    ///     <para>
    ///         <b>Leased/offered/reserved counts</b>: read fresh from
    ///         <see cref="Ninlil.Dhcp.Data.Abstractions.Repository.ILeaseRepository.ListByScopeNameAsync" />/
    ///         <see cref="Ninlil.Dhcp.Data.Abstractions.Repository.IDhcpReservationRepository" />
    ///         and counted directly -- see <see cref="DhcpScopeStatistics" />'s own doc comment for
    ///         each field's exact definition.
    ///     </para>
    ///     <para>
    ///         <b>Available slots</b>: computed by first building the distinct, de-duplicated set
    ///         of slots (by their own normalized address string) currently occupied by either a
    ///         blocking lease (the same "not <c>Released</c>, and not yet past its own
    ///         <c>LeaseExpiresAtUtc</c>" rule <c>Allocation.Dhcpv4LeaseAllocationService.IsLeaseBlocking</c>
    ///         already applies) or a currently-enabled reservation, keeping only the ones that fall
    ///         within the scope's own configured range -- an out-of-range reservation (a
    ///         deliberately supported case, see <c>DhcpReservationEntity</c>'s own doc comment)
    ///         never counts against this scope's own total. This deliberate de-duplication (rather
    ///         than a naive <c>TotalSlots - LeasedCount - ReservedCount</c>) avoids double-counting
    ///         the common case where a client's own reserved address is also its own currently
    ///         active lease.
    ///     </para>
    /// </summary>
    /// <param name="scope">The scope to compute current utilization for.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The scope's own real, freshly-computed utilization.</returns>
    Task<DhcpScopeStatistics> ComputeAsync(
        DhcpScopeEntity scope,
        CancellationToken cancellationToken = default
    );
}
