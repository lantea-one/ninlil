using System.Net;
using System.Numerics;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Network;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Statistics;

/// <summary>
///     This class implements <see cref="IDhcpScopeStatisticsService" /> on
///     <see cref="ILeaseRepository" /> and <see cref="IDhcpReservationRepository" /> (see the
///     interface for the computation). <c>Program.cs</c> registers it for every configuration,
///     like <see cref="Administration.DhcpLeaseAdministrationService" />, since scope utilization
///     can be reported whichever engine (local or relay) is active.
/// </summary>
/// <param name="leaseRepository">The repository a scope's leases are read through.</param>
/// <param name="reservationRepository">The repository a scope's reservations are read through.</param>
public sealed class DhcpScopeStatisticsService(
    ILeaseRepository leaseRepository,
    IDhcpReservationRepository reservationRepository
) : IDhcpScopeStatisticsService
{
    /// <inheritdoc />
    public async Task<DhcpScopeStatistics> ComputeAsync(
        DhcpScopeEntity scope,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(scope);

        BigInteger totalSlots = ComputeTotalSlots(scope);

        IReadOnlyList<LeaseEntity> leases = await leaseRepository.ListByScopeNameAsync(
            scope.Name,
            cancellationToken
        );
        IReadOnlyList<DhcpReservationEntity> reservations = await reservationRepository.ListAsync(
            reservation => reservation.ScopeId == scope.Id,
            cancellationToken
        );

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Check the expiry time as well as the state: State only becomes Expired when
        // Dhcpv4LeaseExpirySweepService next runs, so a just-expired lease can still read
        // Active or Offered.
        int leasedCount = leases.Count(lease =>
            lease.State == LeaseStateEnum.Active && IsBlocking(lease, now)
        );
        int offeredCount = leases.Count(lease =>
            lease.State == LeaseStateEnum.Offered && IsBlocking(lease, now)
        );
        List<DhcpReservationEntity> enabledReservations =
        [
            .. reservations.Where(reservation => !reservation.IsDisabled),
        ];
        int reservedCount = enabledReservations.Count;

        // A set of in-range occupied addresses, so a reserved address that is also leased counts
        // once (see IDhcpScopeStatisticsService). Addresses are compared as stored strings.
        HashSet<string> occupiedWithinRange = new(StringComparer.OrdinalIgnoreCase);

        foreach (LeaseEntity lease in leases)
        {
            if (!IsBlocking(lease, now))
                continue;

            if (IsWithinScopeRange(scope, lease.IpAddress))
                occupiedWithinRange.Add(lease.IpAddress);
        }

        foreach (DhcpReservationEntity reservation in enabledReservations)
        {
            if (IsWithinScopeRange(scope, reservation.IpAddress))
                occupiedWithinRange.Add(reservation.IpAddress);
        }

        BigInteger available = totalSlots - occupiedWithinRange.Count;
        if (available < BigInteger.Zero)
            available = BigInteger.Zero;

        return new DhcpScopeStatistics(
            scope.Id,
            totalSlots,
            leasedCount,
            offeredCount,
            reservedCount,
            available
        );
    }

    /// <summary>
    ///     This method reports whether a lease holds its address, by the rule of
    ///     <c>Allocation.Dhcpv4LeaseAllocationService.IsLeaseBlocking</c>: not
    ///     <see cref="LeaseStateEnum.Released" /> and not past <c>LeaseExpiresAtUtc</c>. A lease
    ///     with no expiry counts as held.
    /// </summary>
    private static bool IsBlocking(LeaseEntity lease, DateTimeOffset now) =>
        lease.State != LeaseStateEnum.Released
        && (!lease.LeaseExpiresAtUtc.HasValue || lease.LeaseExpiresAtUtc.Value > now);

    /// <summary>
    ///     This method reports whether an address is within the scope's range, using the IPv4 or
    ///     IPv6 helper. An address or range that does not parse (possible for imported data) is
    ///     treated as out of range.
    /// </summary>
    private static bool IsWithinScopeRange(DhcpScopeEntity scope, string address)
    {
        if (
            string.IsNullOrEmpty(address)
            || !IPAddress.TryParse(address, out IPAddress parsedAddress)
            || !IPAddress.TryParse(scope.RangeStartAddress, out IPAddress rangeStart)
            || !IPAddress.TryParse(scope.RangeEndAddress, out IPAddress rangeEnd)
        )
            return false;

        return scope.ProtocolVersion == InternetProtocolVersionEnum.IPv6
            ? Dhcpv6NetworkHelper.IsWithinRange(parsedAddress, rangeStart, rangeEnd)
            : Dhcpv4NetworkHelper.IsWithinRange(parsedAddress, rangeStart, rangeEnd);
    }

    /// <summary>
    ///     This method computes the number of slots in the scope's range (see
    ///     <see cref="IDhcpScopeStatisticsService.ComputeAsync" /> for the rule per
    ///     <see cref="DhcpScopeKindEnum" />). A range that does not parse or ends before it starts
    ///     gives zero.
    /// </summary>
    private static BigInteger ComputeTotalSlots(DhcpScopeEntity scope)
    {
        if (
            !IPAddress.TryParse(scope.RangeStartAddress, out IPAddress rangeStart)
            || !IPAddress.TryParse(scope.RangeEndAddress, out IPAddress rangeEnd)
        )
            return BigInteger.Zero;

        if (scope.ProtocolVersion != InternetProtocolVersionEnum.IPv6)
        {
            uint start = Dhcpv4NetworkHelper.ToUInt32(rangeStart);
            uint end = Dhcpv4NetworkHelper.ToUInt32(rangeEnd);
            return end < start ? BigInteger.Zero : new BigInteger(end) - start + 1;
        }

        BigInteger startValue = Dhcpv6NetworkHelper.ToBigInteger(rangeStart);
        BigInteger endValue = Dhcpv6NetworkHelper.ToBigInteger(rangeEnd);

        if (
            scope.Kind == DhcpScopeKindEnum.PrefixDelegation
            && scope.DelegatedPrefixLength is { } delegatedPrefixLength
        )
        {
            // Count prefixes with the start rounded down to a prefix boundary, as
            // Dhcpv6NetworkHelper.EnumeratePrefixes does, without its scan cap.
            BigInteger blockSize = BigInteger.One << (128 - delegatedPrefixLength);
            BigInteger mask = Dhcpv6NetworkHelper.ComputeMask(delegatedPrefixLength);
            BigInteger alignedStart = startValue & mask;

            return endValue < alignedStart
                ? BigInteger.Zero
                : (endValue - alignedStart) / blockSize + 1;
        }

        return endValue < startValue ? BigInteger.Zero : endValue - startValue + 1;
    }
}
