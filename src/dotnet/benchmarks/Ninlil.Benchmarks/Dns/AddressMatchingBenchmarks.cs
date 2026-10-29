using System.Linq.Expressions;
using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.AccessControl;
using Ninlil.Dns.Server.ClientIdentity;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.RateLimiting;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures the address and network matching that runs for every DNS query: 100
///     filter group networks, a 100-entry response-IP list, 50 static address mappings, 20
///     access-control rules, and the rate limiter with 20 subnet-bypass networks and a tracked
///     client. Each benchmark is the matching cost of one query for a client that matches the
///     last entry (so every entry is tried), against in-memory fakes in place of the database.
///     <para>
///         The setup checks every result once, so a wrong data set fails the run instead of
///         measuring nothing. Nothing here touches a socket or a database, but
///         <c>Program</c> still prepares the selected engines, so run it with
///         <c>--engines Sqlite</c>.
///     </para>
/// </summary>
[Config(typeof(StorageConfig))]
public class AddressMatchingBenchmarks
{
    private static readonly IPAddress Client = IPAddress.Parse("192.0.2.9");

    private string[] _groupCidrs;
    private NetworkEntry[] _groupNetworks;
    private DnsFilterListIndex _responseIpList;
    private StaticAddressMappingClientIdentityResolver _mappings;
    private DnsAccessControlRuleCache _accessControl;
    private TokenBucketDnsRateLimiter _limiter;

    /// <summary>
    ///     This method builds the data and checks that the client matches the last entry of each.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _groupCidrs = [.. Enumerable.Range(0, 99).Select(i => $"10.{i}.0.0/16"), "192.0.2.0/24"];
        _groupNetworks =
        [
            .. _groupCidrs.Select(cidr =>
                NetworkEntry.TryParse(cidr, out NetworkEntry network) ? network : null
            ),
        ];

        _responseIpList = DnsFilterListIndex.Build(
            DnsFilterListKindEnum.ResponseIp,
            [
                .. Enumerable
                    .Range(0, 99)
                    .Select(i => new DnsFilterListEntryEntity { Value = $"172.16.{i}.0/24" }),
                new DnsFilterListEntryEntity { Value = "192.0.2.0/24" },
            ]
        );

        ServiceCollection services = new();
        services.AddSingleton<IDnsClientStaticAddressMappingRepository>(
            new FakeMappingRepository([
                .. Enumerable
                    .Range(0, 49)
                    .Select(i => new DnsClientStaticAddressMappingEntity
                    {
                        Cidr = $"10.100.{i}.0/24",
                        ClientIdentifier = $"client-{i}",
                    }),
                new DnsClientStaticAddressMappingEntity
                {
                    Cidr = "192.0.2.0/24",
                    ClientIdentifier = "last",
                },
            ])
        );
        services.AddSingleton<IDnsAccessControlRuleRepository>(
            new FakeRuleRepository([
                .. Enumerable
                    .Range(0, 19)
                    .Select(i => new DnsAccessControlRuleEntity
                    {
                        Cidr = $"10.200.{i}.0/24",
                        Capabilities = DnsAccessControlCapabilityEnum.RecursionAccess,
                    }),
                new DnsAccessControlRuleEntity
                {
                    Cidr = "192.0.2.0/24",
                    Capabilities = DnsAccessControlCapabilityEnum.RecursionAccess,
                },
            ])
        );
        ServiceProvider provider = services.BuildServiceProvider();

        _mappings = new StaticAddressMappingClientIdentityResolver(
            provider.GetRequiredService<IServiceScopeFactory>()
        );
        await _mappings.RefreshAsync();

        _accessControl = new DnsAccessControlRuleCache(
            provider.GetRequiredService<IServiceScopeFactory>()
        );
        await _accessControl.ReloadAsync();

        // A subnet limit of 0, so a client is dropped unless the bypass list exempts it, and a
        // bypass list of 20 networks whose last entry covers the client, so the check tries
        // every entry.
        _limiter = new TokenBucketDnsRateLimiter(
            Options.Create(
                new DnsRateLimitOptions
                {
                    Enable = true,
                    BurstSize = int.MaxValue,
                    QueriesPerSecond = 1000,
                    SubnetUdpQueryLimit = 0,
                    SubnetSampleWindow = TimeSpan.FromHours(1),
                    SubnetTruncatePercentage = 0,
                    SubnetBypassCidrs =
                    [
                        .. Enumerable.Range(0, 19).Select(i => $"10.50.{i}.0/24"),
                        "192.0.2.0/24",
                    ],
                }
            )
        );

        Expect(_responseIpList.ContainsAddress(Client), "the response-IP list matches the client");
        Expect(GroupNetworksParsePerQuery(), "the last group network matches the client");
        Expect(GroupNetworksParsed(), "the parsed last group network matches the client");
        Expect(
            (await _mappings.ResolveAsync(Client))?.ClientIdentifier == "last",
            "the last static mapping resolves the client"
        );
        Expect(
            _accessControl.HasCapability(Client, DnsAccessControlCapabilityEnum.RecursionAccess),
            "the last access rule grants the client recursion"
        );
        Expect(await RateLimiterBypass(), "the last bypass network exempts the client");
        Expect(await RateLimiterTrackedClient(), "a tracked client is let through");
    }

    /// <summary>The group network scan as the text-parsing call does it: parse each CIDR per query.</summary>
    [Benchmark]
    public bool GroupNetworksParsePerQuery()
    {
        foreach (string cidr in _groupCidrs)
        {
            if (ClientAddress.NetworkContains(cidr, Client))
                return true;
        }

        return false;
    }

    /// <summary>The group network scan over networks parsed once.</summary>
    [Benchmark]
    public bool GroupNetworksParsed()
    {
        foreach (NetworkEntry network in _groupNetworks)
        {
            if (network.Contains(Client))
                return true;
        }

        return false;
    }

    /// <summary>A 100-entry response-IP list tested with the client address.</summary>
    [Benchmark]
    public bool ResponseIpList() => _responseIpList.ContainsAddress(Client);

    /// <summary>The 50 static address mappings scanned for the client.</summary>
    [Benchmark]
    public async Task<bool> StaticMappings() => await _mappings.ResolveAsync(Client) is not null;

    /// <summary>The 20 access-control rules scanned for a capability of the client.</summary>
    [Benchmark]
    public bool AccessControlRules() =>
        _accessControl.HasCapability(Client, DnsAccessControlCapabilityEnum.RecursionAccess);

    /// <summary>The rate limiter's subnet check with 20 bypass networks.</summary>
    [Benchmark]
    public async Task<bool> RateLimiterBypass() =>
        await _limiter.CheckSubnetLimitAsync(Client, isUdp: true)
        == DnsSubnetRateLimitDecisionEnum.Allow;

    /// <summary>The rate limiter's per-client token bucket for an already tracked client.</summary>
    [Benchmark]
    public async Task<bool> RateLimiterTrackedClient() => await _limiter.TryAcquireAsync(Client);

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(
                $"Benchmark setup check failed: {description}. The data set or the matching does not behave as this benchmark expects."
            );
    }

    /// <summary>
    ///     This class is an in-memory <see cref="IDnsClientStaticAddressMappingRepository" /> that
    ///     answers only <c>ListEnabledAsync</c>, the one call the resolver's load makes, so the
    ///     benchmark measures matching and not a database.
    /// </summary>
    private sealed class FakeMappingRepository(List<DnsClientStaticAddressMappingEntity> rows)
        : IDnsClientStaticAddressMappingRepository
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> ListEnabledAsync(
            CancellationToken stoppingToken = default
        ) => Task.FromResult<IReadOnlyList<DnsClientStaticAddressMappingEntity>>([.. rows]);

        /// <inheritdoc />
        public Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> SearchAsync(
            int skip,
            int take,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<int> CountAsync(CancellationToken stoppingToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<DnsClientStaticAddressMappingEntity> GetByIdAsync(
            Guid id,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> ListAsync(
            Expression<Func<DnsClientStaticAddressMappingEntity, bool>> predicate = null,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task AddAsync(
            DnsClientStaticAddressMappingEntity entity,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Update(DnsClientStaticAddressMappingEntity entity) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public void Remove(DnsClientStaticAddressMappingEntity entity) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///     This class is an in-memory <see cref="IDnsAccessControlRuleRepository" /> that answers
    ///     only <c>ListEnabledAsync</c>, the one call the rule cache's load makes.
    /// </summary>
    private sealed class FakeRuleRepository(List<DnsAccessControlRuleEntity> rows)
        : IDnsAccessControlRuleRepository
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<DnsAccessControlRuleEntity>> ListEnabledAsync(
            CancellationToken stoppingToken = default
        ) => Task.FromResult<IReadOnlyList<DnsAccessControlRuleEntity>>([.. rows]);

        /// <inheritdoc />
        public Task<IReadOnlyList<DnsAccessControlRuleEntity>> SearchAsync(
            int skip,
            int take,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<int> CountAsync(CancellationToken stoppingToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<DnsAccessControlRuleEntity> GetByIdAsync(
            Guid id,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<IReadOnlyList<DnsAccessControlRuleEntity>> ListAsync(
            Expression<Func<DnsAccessControlRuleEntity, bool>> predicate = null,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task AddAsync(
            DnsAccessControlRuleEntity entity,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Update(DnsAccessControlRuleEntity entity) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Remove(DnsAccessControlRuleEntity entity) => throw new NotSupportedException();
    }
}
