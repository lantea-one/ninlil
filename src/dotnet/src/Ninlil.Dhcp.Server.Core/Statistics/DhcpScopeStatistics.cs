using System.Numerics;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Statistics;

/// <summary>
///     This record carries one scope's current utilization, computed by
///     <see cref="IDhcpScopeStatisticsService" /> (which describes each field's derivation).
///     <see cref="TotalSlots" /> and <see cref="AvailableSlots" /> are <see cref="BigInteger" />
///     because an IPv6 address range can hold far more than <see cref="long.MaxValue" />
///     addresses (a <c>/64</c> holds 2^64), which is also why <c>Ninlil.Web</c> sends them as
///     decimal strings.
/// </summary>
/// <param name="ScopeId">The identifier of the scope these figures describe.</param>
/// <param name="TotalSlots">
///     The number of slots in the scope's range: addresses for an <c>AddressAssignment</c>
///     scope, block-aligned prefixes for a <c>PrefixDelegation</c> scope (see
///     <see cref="IDhcpScopeStatisticsService" />). Zero when <c>RangeStartAddress</c> or
///     <c>RangeEndAddress</c> does not parse or the end comes before the start.
/// </param>
/// <param name="LeasedCount">
///     The number of <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" /> leases that have
///     not yet expired.
/// </param>
/// <param name="OfferedCount">
///     The number of <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Offered" /> leases (a
///     DISCOVER or SOLICIT answered, no REQUEST yet) still within their offer hold; each holds a
///     slot until the hold ends.
/// </param>
/// <param name="ReservedCount">
///     The number of enabled reservations in the scope, whether or not their address is in the
///     range. Disabled reservations
///     (<see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpReservationEntity.IsDisabled" />) are
///     not counted.
/// </param>
/// <param name="AvailableSlots">
///     <see cref="TotalSlots" /> minus the distinct in-range addresses held by a lease (including
///     a declined address in quarantine) or an enabled reservation. It is never negative: an
///     over-committed scope (after its range was narrowed, say) reports zero.
/// </param>
public sealed record DhcpScopeStatistics(
    Guid ScopeId,
    BigInteger TotalSlots,
    int LeasedCount,
    int OfferedCount,
    int ReservedCount,
    BigInteger AvailableSlots
);
