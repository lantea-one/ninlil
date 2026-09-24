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
///     This class implements <see cref="IDhcpScopeStatisticsService" /> against the real
///     <see cref="ILeaseRepository" />/<see cref="IDhcpReservationRepository" /> repositories --
///     see that interface's own doc comment for the exact computation. Deliberately registered
///     unconditionally, alongside <see cref="Administration.DhcpLeaseAdministrationService" />,
///     rather than folded into either protocol's own local-engine registration -- a scope's own
///     utilization is meaningful to report regardless of which specific DHCPv4/DHCPv6 engine
///     variant (local or relay) is currently active.
/// </summary>
/// <param name="leaseRepository">The repository this service reads a scope's own leases through.</param>
/// <param name="reservationRepository">The repository this service reads a scope's own reservations through.</param>
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

        // Both counts also re-check IsBlocking's own "not yet past LeaseExpiresAtUtc" half,
        // rather than trusting State alone -- a lease's own State only flips to Expired when
        // Dhcpv4LeaseExpirySweepService's own periodic sweep next runs, so a lease that expired
        // moments ago can still legitimately read back as Active/Offered until that sweep catches
        // up; this keeps both counts genuinely current rather than merely current-as-of-the-last-
        // sweep.
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

        // De-duplicated, within-range occupied-slot set -- see IDhcpScopeStatisticsService's own
        // doc comment for why Available is computed this way rather than a naive subtraction that
        // would double-count a reservation that is also a currently active lease.
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
    ///     This method reports whether a lease is currently occupying its own address at all --
    ///     the same rule <c>Allocation.Dhcpv4LeaseAllocationService.IsLeaseBlocking</c> already
    ///     applies: not <see cref="LeaseStateEnum.Released" />, and not yet past its own
    ///     <c>LeaseExpiresAtUtc</c> (a lease with no expiry at all, e.g. a brand-new row mid-DORA,
    ///     is treated as still held).
    /// </summary>
    private static bool IsBlocking(LeaseEntity lease, DateTimeOffset now) =>
        lease.State != LeaseStateEnum.Released
        && (!lease.LeaseExpiresAtUtc.HasValue || lease.LeaseExpiresAtUtc.Value > now);

    /// <summary>
    ///     This method reports whether an address string falls within a scope's own configured
    ///     range, dispatching to the correct protocol-specific arithmetic. An address that fails to
    ///     parse (should never happen for a row this project's own engine wrote, but is possible
    ///     for hand-seeded/imported data) is treated as out of range rather than throwing.
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
    ///     This method computes the total number of slots a scope's own configured range holds --
    ///     see <see cref="IDhcpScopeStatisticsService.ComputeAsync" />'s own doc comment for the
    ///     exact rule per <see cref="DhcpScopeKindEnum" />. Returns zero for a range that fails to
    ///     parse, or is inverted (the end address numerically precedes the start address) --
    ///     genuinely broken/unset configuration reports no capacity rather than throwing.
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
            // Block-aligned prefix count, mirroring Dhcpv6NetworkHelper.EnumeratePrefixes's own
            // "align the first candidate down to a block boundary" rule exactly, minus that
            // method's own bounded-scan cap -- see this class's own doc comment.
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
