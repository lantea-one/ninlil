using System.Numerics;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Statistics;

/// <summary>
///     This record carries a single DHCP scope's own genuine, freshly-computed utilization
///     figures -- see <see cref="IDhcpScopeStatisticsService" />'s own doc comment for exactly how
///     each field is derived. <see cref="TotalSlots" />/<see cref="AvailableSlots" /> are
///     <see cref="BigInteger" /> rather than a built-in integer type since an IPv6
///     <c>AddressAssignment</c> scope's own configured range can legitimately hold far more than
///     <see cref="long.MaxValue" /> addresses (an ordinary <c>/64</c> alone holds 2^64 of them) --
///     <c>Ninlil.Web</c>'s own REST representation carries both as a decimal string for exactly
///     this reason.
/// </summary>
/// <param name="ScopeId">The identifier of the scope these figures describe.</param>
/// <param name="TotalSlots">
///     The total number of slots the scope's own configured range holds -- an ordinary address, for
///     an <c>AddressAssignment</c> scope, or a block-aligned delegable prefix, for a
///     <c>PrefixDelegation</c> scope (see <see cref="IDhcpScopeStatisticsService" />'s own doc
///     comment for the exact block-alignment rule). Zero when the scope's own
///     <c>RangeStartAddress</c>/<c>RangeEndAddress</c> fail to parse, or the range is inverted
///     (<c>RangeEndAddress</c> numerically before <c>RangeStartAddress</c>).
/// </param>
/// <param name="LeasedCount">
///     The number of currently <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Active" /> leases
///     this scope has granted right now.
/// </param>
/// <param name="OfferedCount">
///     The number of leases currently tentatively
///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Offered" /> (a DISCOVER/SOLICIT answered,
///     but no REQUEST/confirmation received yet) and not yet past their own offer-hold window --
///     each one still occupies a real slot for as long as that hold lasts.
/// </param>
/// <param name="ReservedCount">
///     The number of this scope's own currently-enabled static reservations -- see
///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpReservationEntity.IsDisabled" />; a
///     disabled reservation returns its address to the ordinary pool and is not counted here.
/// </param>
/// <param name="AvailableSlots">
///     <see cref="TotalSlots" /> minus every distinct slot currently occupied by a blocking lease
///     or an enabled reservation that falls within the scope's own configured range -- never
///     negative (an over-committed scope, e.g. after its own range was narrowed, reports zero
///     rather than a negative figure).
/// </param>
public sealed record DhcpScopeStatistics(
    Guid ScopeId,
    BigInteger TotalSlots,
    int LeasedCount,
    int OfferedCount,
    int ReservedCount,
    BigInteger AvailableSlots
);
