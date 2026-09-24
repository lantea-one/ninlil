using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Statistics;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test proves <see cref="IDhcpScopeStatisticsService.ComputeAsync" /> against real,
///     persisted lease/reservation rows across both DHCPv4 (a small, hand-countable
///     <c>AddressAssignment</c> pool) and DHCPv6 (an <c>AddressAssignment</c> pool, plus a
///     <c>PrefixDelegation</c> pool exercising this service's own <see cref="BigInteger" />
///     block-alignment arithmetic).
/// </summary>
public sealed class DhcpScopeStatisticsServiceTests : IAsyncLifetime
{
    private Dhcpv4EngineTestHost _v4Host;
    private Dhcpv6EngineTestHost _v6Host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _v4Host = new Dhcpv4EngineTestHost();
        await _v4Host.InitializeAsync();

        _v6Host = new Dhcpv6EngineTestHost();
        await _v6Host.InitializeAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _v4Host.DisposeAsync();
        await _v6Host.DisposeAsync();
    }

    [Fact]
    public async Task ComputeAsync_ForAnIPv4AddressAssignmentScope_ComputesGenuineFigures()
    {
        DhcpScopeEntity scope = await _v4Host.SeedScopeAsync(
            "stats-v4-scope",
            "192.0.2.0",
            "192.0.2.9"
        );

        DhcpScopeStatistics empty = await _v4Host.ComputeStatisticsAsync(scope);
        Assert.Equal(new BigInteger(10), empty.TotalSlots);
        Assert.Equal(0, empty.LeasedCount);
        Assert.Equal(0, empty.OfferedCount);
        Assert.Equal(0, empty.ReservedCount);
        Assert.Equal(new BigInteger(10), empty.AvailableSlots);

        await using (AsyncServiceScope diScope = _v4Host.Host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                diScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IDhcpReservationRepository reservationRepository =
                diScope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();
            IUnitOfWork unitOfWork = diScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scope.Name,
                    ClientIdentifier = "aa:bb:cc:dd:ee:01",
                    IpAddress = "192.0.2.1",
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                }
            );

            // A lease whose own expiry has already passed no longer blocks the pool, even though
            // its own State column has not yet been swept to Expired.
            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scope.Name,
                    ClientIdentifier = "aa:bb:cc:dd:ee:02",
                    IpAddress = "192.0.2.2",
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                }
            );

            await reservationRepository.AddAsync(
                new DhcpReservationEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ScopeId = scope.Id,
                    ClientIdentifier = "aa:bb:cc:dd:ee:03",
                    IpAddress = "192.0.2.3",
                    IsDisabled = false,
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        DhcpScopeStatistics populated = await _v4Host.ComputeStatisticsAsync(scope);
        Assert.Equal(new BigInteger(10), populated.TotalSlots);
        Assert.Equal(1, populated.LeasedCount);
        Assert.Equal(1, populated.ReservedCount);
        // 10 total, minus 2 genuinely occupied slots (the still-active lease and the reservation)
        // -- the already-expired-but-not-yet-swept lease does not count.
        Assert.Equal(new BigInteger(8), populated.AvailableSlots);
    }

    [Fact]
    public async Task ComputeAsync_ForAnOverCommittedScope_NeverReportsNegativeAvailability()
    {
        // A range narrowed to a single address after a lease already outside it was granted --
        // TotalSlots is 1, but the occupied set can still exceed that; Available must clamp at 0.
        DhcpScopeEntity scope = await _v4Host.SeedScopeAsync(
            "stats-overcommit-scope",
            "192.0.2.50",
            "192.0.2.50"
        );

        await using (AsyncServiceScope diScope = _v4Host.Host.Services.CreateAsyncScope())
        {
            IDhcpReservationRepository reservationRepository =
                diScope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>();
            IUnitOfWork unitOfWork = diScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await reservationRepository.AddAsync(
                new DhcpReservationEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ScopeId = scope.Id,
                    ClientIdentifier = "aa:bb:cc:dd:ee:10",
                    IpAddress = "192.0.2.50",
                    IsDisabled = false,
                }
            );
            await reservationRepository.AddAsync(
                new DhcpReservationEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ScopeId = scope.Id,
                    ClientIdentifier = "aa:bb:cc:dd:ee:11",
                    // Deliberately outside the (narrowed) range -- a reservation may legitimately
                    // sit outside a scope's own dynamic pool, so this must be excluded entirely
                    // rather than ever pushing Available negative.
                    IpAddress = "192.0.2.99",
                    IsDisabled = false,
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        DhcpScopeStatistics statistics = await _v4Host.ComputeStatisticsAsync(scope);
        Assert.Equal(BigInteger.One, statistics.TotalSlots);
        // Both reservations count toward ReservedCount (it is not range-filtered), but only the
        // in-range one occupies Available's own accounting.
        Assert.Equal(2, statistics.ReservedCount);
        Assert.Equal(BigInteger.Zero, statistics.AvailableSlots);
    }

    [Fact]
    public async Task ComputeAsync_ForAnIPv6AddressAssignmentScope_UsesBigIntegerArithmetic()
    {
        // An ordinary /64 -- 2^64 addresses, far larger than any built-in integer type, proving
        // this is genuine BigInteger arithmetic rather than a truncated approximation.
        DhcpScopeEntity scope = await _v6Host.SeedAddressScopeAsync(
            "stats-v6-scope",
            "2001:db8:1::",
            "2001:db8:1:0:ffff:ffff:ffff:ffff",
            subnetPrefixLength: 64
        );

        DhcpScopeStatistics statistics = await _v6Host.ComputeStatisticsAsync(scope);

        BigInteger expectedTotal = BigInteger.Pow(2, 64);
        Assert.Equal(expectedTotal, statistics.TotalSlots);
        Assert.Equal(expectedTotal, statistics.AvailableSlots);
        Assert.Equal(0, statistics.LeasedCount);
        Assert.Equal(0, statistics.ReservedCount);
    }

    [Fact]
    public async Task ComputeAsync_ForAPrefixDelegationScope_CountsBlockAlignedPrefixes()
    {
        // A pool delegating whole /64 blocks (delegatedPrefixLength=64, so each block is exactly
        // one ordinary /64 -- easy to reason about by hand), spanning a /62 range
        // (2001:db8:2:0::/64 through 2001:db8:2:3::/64) -- exactly 4 delegable blocks.
        DhcpScopeEntity scope = await _v6Host.SeedPrefixDelegationScopeAsync(
            "stats-v6-pd-scope",
            "2001:db8:2::",
            "2001:db8:2:3:ffff:ffff:ffff:ffff",
            delegatedPrefixLength: 64,
            subnetPrefixLength: 62
        );

        DhcpScopeStatistics empty = await _v6Host.ComputeStatisticsAsync(scope);
        Assert.Equal(new BigInteger(4), empty.TotalSlots);
        Assert.Equal(new BigInteger(4), empty.AvailableSlots);

        // Grant a real delegated prefix through the actual DHCPv6 stateful allocation engine to
        // confirm the statistics service's own dedup logic reaches a genuinely-block-aligned
        // lease row correctly.
        await using (AsyncServiceScope diScope = _v6Host.Host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                diScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = diScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv6,
                    ScopeName = scope.Name,
                    ClientIdentifier = "00:01:00:01:2c:8b:9c:3d:aa:bb:cc:dd:ee:01",
                    IpAddress = "2001:db8:2::",
                    PrefixLength = 64,
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        DhcpScopeStatistics afterGrant = await _v6Host.ComputeStatisticsAsync(scope);
        Assert.Equal(new BigInteger(4), afterGrant.TotalSlots);
        Assert.Equal(1, afterGrant.LeasedCount);
        Assert.Equal(new BigInteger(3), afterGrant.AvailableSlots);
    }
}
