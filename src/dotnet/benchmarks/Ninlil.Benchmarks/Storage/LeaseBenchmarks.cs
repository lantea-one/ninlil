using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures the lease queries behind the DNS integration and the DHCP engines:
///     the active-lease snapshot, listing one scope (what allocation does per packet), and the
///     expiry sweep's query.
/// </summary>
[Config(typeof(StorageConfig))]
public class LeaseBenchmarks : StorageBenchmarkBase
{
    /// <summary>The active-lease snapshot (<c>ListActiveAsync</c>).</summary>
    [Benchmark]
    public Task<int> ListActive() =>
        InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<ILeaseRepository>()
                    .ListActiveAsync(DateTimeOffset.UtcNow)
            ).Count
        );

    /// <summary>Every lease of one scope.</summary>
    [Benchmark]
    public Task<int> ListByScopeName() =>
        InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<ILeaseRepository>()
                    .ListByScopeNameAsync($"{BenchmarkDataSet.ScopeNamePrefix}0")
            ).Count
        );

    /// <summary>
    ///     The query of <c>Dhcpv4LeaseExpirySweepService.SweepAsync</c>: active leases whose
    ///     expiry has passed.
    /// </summary>
    [Benchmark]
    public Task<int> ExpirySweepQuery() =>
        InScopeAsync(async provider =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return (
                await provider
                    .GetRequiredService<ILeaseRepository>()
                    .ListAsync(lease =>
                        lease.State == LeaseStateEnum.Active
                        && lease.LeaseExpiresAtUtc != null
                        && lease.LeaseExpiresAtUtc <= now
                    )
            ).Count;
        });
}
