using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures the query-log page and count queries the admin console and the
///     client-activity and cluster endpoints run, against the seeded log: an unfiltered first
///     page, each filter on its own, and a deep page. Filters use the busiest client, identifier
///     and name group of the Zipf data, the worst case for a page that must be sorted.
/// </summary>
[Config(typeof(StorageConfig))]
public class QueryLogSearchBenchmarks : StorageBenchmarkBase
{
    private const int PageSize = 50;

    private static readonly DnsQueryLogSearchCriteria Unfiltered = new();

    private static readonly DnsQueryLogSearchCriteria ByClientAddress = new(
        ClientAddress: BenchmarkDataSet.ClientAddress(0)
    );

    private static readonly DnsQueryLogSearchCriteria ByClientIdentifier = new(
        ClientIdentifier: BenchmarkDataSet.ClientIdentifier(0),
        ClientProtocolVersion: InternetProtocolVersionEnum.IPv4
    );

    private static readonly DnsQueryLogSearchCriteria ByNxDomain = new(
        ResponseCode: DnsResponseCodeEnum.NxDomain
    );

    private static readonly DnsQueryLogSearchCriteria ByDomain = new(
        DomainContains: "example7.test"
    );

    private Task<int> Search(DnsQueryLogSearchCriteria criteria, int skip) =>
        InScopeAsync(async provider =>
            (
                await provider
                    .GetRequiredService<IDnsQueryLogRepository>()
                    .SearchAsync(criteria, skip, PageSize)
            ).Count
        );

    private Task<int> Count(DnsQueryLogSearchCriteria criteria) =>
        InScopeAsync(provider =>
            provider.GetRequiredService<IDnsQueryLogRepository>().CountAsync(criteria)
        );

    /// <summary>First page, no filter.</summary>
    [Benchmark]
    public Task<int> SearchUnfiltered() => Search(Unfiltered, 0);

    /// <summary>First page, by the busiest client address.</summary>
    [Benchmark]
    public Task<int> SearchByClientAddress() => Search(ByClientAddress, 0);

    /// <summary>First page, by the busiest client identifier and IP version.</summary>
    [Benchmark]
    public Task<int> SearchByClientIdentifier() => Search(ByClientIdentifier, 0);

    /// <summary>First page, NXDOMAIN answers only.</summary>
    [Benchmark]
    public Task<int> SearchByNxDomain() => Search(ByNxDomain, 0);

    /// <summary>First page, domain substring.</summary>
    [Benchmark]
    public Task<int> SearchByDomainSubstring() => Search(ByDomain, 0);

    /// <summary>A page at skip 10,000 (or the last tenth of a small log), no filter.</summary>
    [Benchmark]
    public Task<int> SearchDeepPage() =>
        Search(Unfiltered, Math.Min(10_000, Shape.QueryLogEntries / 2));

    /// <summary>Total count, no filter.</summary>
    [Benchmark]
    public Task<int> CountUnfiltered() => Count(Unfiltered);

    /// <summary>Count by the busiest client address.</summary>
    [Benchmark]
    public Task<int> CountByClientAddress() => Count(ByClientAddress);

    /// <summary>Count by the busiest client identifier and IP version.</summary>
    [Benchmark]
    public Task<int> CountByClientIdentifier() => Count(ByClientIdentifier);

    /// <summary>Count of NXDOMAIN answers.</summary>
    [Benchmark]
    public Task<int> CountByNxDomain() => Count(ByNxDomain);

    /// <summary>Count by domain substring.</summary>
    [Benchmark]
    public Task<int> CountByDomainSubstring() => Count(ByDomain);
}
