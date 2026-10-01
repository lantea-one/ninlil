using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Statistics;

/// <summary>
///     This interface computes how much of a scope's range is free and how much is taken, from
///     the scope's configuration and its lease and reservation rows. Nothing is cached: every
///     call reads the rows again. <c>DhcpScopesController</c> serves the result.
/// </summary>
public interface IDhcpScopeStatisticsService
{
    /// <summary>
    ///     This method computes the current utilization of <paramref name="scope" />.
    ///     <para>
    ///         <b>Total slots.</b> For an
    ///         <see cref="Ninlil.Dhcp.Data.Abstractions.Enum.DhcpScopeKindEnum.AddressAssignment" />
    ///         scope, the number of addresses from <c>RangeStartAddress</c> to
    ///         <c>RangeEndAddress</c> inclusive. For an IPv6
    ///         <see cref="Ninlil.Dhcp.Data.Abstractions.Enum.DhcpScopeKindEnum.PrefixDelegation" />
    ///         scope, the number of <c>DelegatedPrefixLength</c>-bit prefixes in the range, with the
    ///         start rounded down to a prefix boundary as
    ///         <c>Network.Dhcpv6NetworkHelper.EnumeratePrefixes</c> does, but without that method's
    ///         <c>MaximumPoolScanCandidates</c> cap.
    ///     </para>
    ///     <para>
    ///         <b>Counts.</b> Leases come from
    ///         <see cref="Ninlil.Dhcp.Data.Abstractions.Repository.ILeaseRepository.ListByScopeNameAsync" />
    ///         and reservations from
    ///         <see cref="Ninlil.Dhcp.Data.Abstractions.Repository.IDhcpReservationRepository" />
    ///         (see <see cref="DhcpScopeStatistics" /> for each field).
    ///     </para>
    ///     <para>
    ///         <b>Available slots.</b> The set of distinct addresses held by a blocking lease (not
    ///         <c>Released</c> and not past <c>LeaseExpiresAtUtc</c>, the rule of
    ///         <c>Allocation.Dhcpv4LeaseAllocationService.IsLeaseBlocking</c>) or an enabled
    ///         reservation is built, keeping only addresses inside the range, and its size is
    ///         subtracted from the total. Using a set means a reserved address that is also leased
    ///         is counted once, and a reservation outside the range does not count.
    ///     </para>
    /// </summary>
    /// <param name="scope">The scope to compute current utilization for.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The scope's current utilization.</returns>
    Task<DhcpScopeStatistics> ComputeAsync(
        DhcpScopeEntity scope,
        CancellationToken cancellationToken = default
    );
}
