using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Rewrites;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;
using Ninlil.Dns.Server.Recursive;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> proves the forwarded-response
///     cache on the real query path: the real <see cref="ForwarderQueryResolver" /> over a real
///     UDP socket to a fake upstream that counts what it receives, and, for the per-client
///     tests, the real <see cref="DnsQueryProcessingPipeline" /> with the real
///     <see cref="DnsFilterEngine" /> in front of it -- so "answered from cache" means the
///     upstream really saw no second query, and "filtered per client" means a real block list
///     really blocked a cached answer for one client and not another.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    private readonly ManualTimeProvider _cacheClock = new();

    private readonly ForwardingOptions _cachingForwardingOptions = new()
    {
        Enable = true,
        FailoverStrategy = ForwarderFailoverStrategyEnum.TryInOrder,
        PerForwarderTimeout = TimeSpan.FromSeconds(2),
        EnableCache = true,
        MaxCacheEntries = 1000,
    };

    private readonly RecursiveServerOptions _cachingRecursiveOptions = new()
    {
        EnableDnssecValidation = false,
        EnableServeStale = false,
    };

    private ForwardedResponseCache CreateResponseCache() =>
        new(
            new StaticOptionsMonitor<ForwardingOptions>(_cachingForwardingOptions),
            new StaticOptionsMonitor<RecursiveServerOptions>(_cachingRecursiveOptions),
            new RecordingEventPublisher(),
            _cacheClock
        );

    private ForwarderQueryResolver CreateCachingResolver(ForwardedResponseCache cache) =>
        CreateResolver(
            forwardingOptions: _cachingForwardingOptions,
            recursiveOptions: _cachingRecursiveOptions,
            responseCache: cache
        );

    private static uint FirstAnswerTtl(DnsQueryResolution resolution) =>
        resolution.Response.Answers[0].TimeToLive;

    private static IPAddress FirstAnswerAddress(DnsMessage response) =>
        (
            (ARecordData)
                DnsRecordDataCodecRegistry.Decode(
                    DnsResourceRecordTypeEnum.A,
                    response.Answers[0].Data
                )
        ).Address;

    [Fact]
    public async Task Cache_ASecondIdenticalQuery_IsAnsweredWithoutGoingUpstream()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        ForwardedResponseCache cache = CreateResponseCache();
        ForwarderQueryResolver resolver = CreateCachingResolver(cache);

        DnsQueryResolution first = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(1, "example.com."))
        );
        _cacheClock.Advance(TimeSpan.FromSeconds(1));
        DnsQueryResolution second = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(2, "EXAMPLE.com."))
        );

        Assert.Equal(1, upstream.QueryCount);
        Assert.True(second.IsHandled);
        Assert.Equal(2, second.Response.Id);
        Assert.Equal(FirstAnswerAddress(first.Response), FirstAnswerAddress(second.Response));

        // The cached copy counts down from the TTL the upstream sent (300 seconds).
        Assert.Equal(300u, FirstAnswerTtl(first));
        Assert.Equal(299u, FirstAnswerTtl(second));
        Assert.Equal(1, cache.GetStatistics().Hits);
    }

    [Fact]
    public async Task Cache_AnExpiredEntry_GoesUpstreamAgain()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        ForwarderQueryResolver resolver = CreateCachingResolver(CreateResponseCache());

        await resolver.TryResolveAsync(BuildContext(BuildQuery(1, "example.com.")));
        _cacheClock.Advance(TimeSpan.FromSeconds(300));
        DnsQueryResolution afterExpiry = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(2, "example.com."))
        );

        Assert.Equal(2, upstream.QueryCount);
        Assert.Equal(300u, FirstAnswerTtl(afterExpiry));
    }

    [Fact]
    public async Task Cache_WhenSwitchedOff_EveryQueryGoesUpstream()
    {
        _cachingForwardingOptions.EnableCache = false;
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        ForwarderQueryResolver resolver = CreateCachingResolver(CreateResponseCache());

        await resolver.TryResolveAsync(BuildContext(BuildQuery(1, "example.com.")));
        await resolver.TryResolveAsync(BuildContext(BuildQuery(2, "example.com.")));

        Assert.Equal(2, upstream.QueryCount);
    }

    [Fact]
    public async Task Cache_AServfail_IsNotCached()
    {
        DnsMessage servfail = new()
        {
            IsResponse = true,
            ResponseCode = DnsResponseCodeEnum.ServerFailure,
        };
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(DnsMessageWriter.Write(servfail));
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        ForwarderQueryResolver resolver = CreateCachingResolver(CreateResponseCache());

        DnsQueryResolution first = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(1, "example.com."))
        );
        await resolver.TryResolveAsync(BuildContext(BuildQuery(2, "example.com.")));

        Assert.Equal(DnsResponseCodeEnum.ServerFailure, first.Response.ResponseCode);
        Assert.Equal(2, upstream.QueryCount);
    }

    [Fact]
    public async Task Cache_WhenEveryForwarderFails_ServesStaleOnlyIfServeStaleIsOn()
    {
        FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        _cachingForwardingOptions.PerForwarderTimeout = TimeSpan.FromMilliseconds(500);
        ForwarderQueryResolver resolver = CreateCachingResolver(CreateResponseCache());

        await resolver.TryResolveAsync(BuildContext(BuildQuery(1, "example.com.")));

        // The upstream goes away, and the cached answer expires.
        upstream.Dispose();
        _cacheClock.Advance(TimeSpan.FromMinutes(10));

        DnsQueryResolution withoutServeStale = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(2, "example.com."))
        );
        Assert.False(withoutServeStale.IsHandled);

        _cachingRecursiveOptions.EnableServeStale = true;
        _cachingRecursiveOptions.MaxStaleServeAge = TimeSpan.FromHours(1);

        DnsQueryResolution withServeStale = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(3, "example.com."))
        );
        Assert.True(withServeStale.IsHandled);
        Assert.Equal(3, withServeStale.Response.Id);
        Assert.Equal(ForwardedResponseCache.StaleAnswerTtlSeconds, FirstAnswerTtl(withServeStale));
    }

    [Fact]
    public async Task Cache_ConditionalForwarderAnswers_DoNotLeakIntoTheGlobalView()
    {
        using FakeUdpUpstream globalUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("203.0.113.1"))
        );
        using FakeUdpUpstream internalUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("10.1.2.3"))
        );
        await SeedForwarderAsync($"127.0.0.1:{globalUpstream.Port}");
        await SeedForwarderZoneAsync("corp.example", $"127.0.0.1:{internalUpstream.Port}");
        ForwarderQueryResolver resolver = CreateCachingResolver(CreateResponseCache());

        // Answered, and cached, through the corp.example forwarder zone.
        DnsQueryResolution viaZone = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(1, "intranet.corp.example."))
        );
        Assert.Equal(IPAddress.Parse("10.1.2.3"), FirstAnswerAddress(viaZone.Response));

        // Once the zone is disabled the same name goes to the global forwarders, and must get
        // their answer rather than the internal one still sitting in the cache.
        await DisableZoneAsync("corp.example");
        DnsQueryResolution viaGlobal = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(2, "intranet.corp.example."))
        );
        Assert.Equal(IPAddress.Parse("203.0.113.1"), FirstAnswerAddress(viaGlobal.Response));
        Assert.Equal(1, internalUpstream.QueryCount);
        Assert.Equal(1, globalUpstream.QueryCount);

        // And each view keeps its own cached answer.
        await resolver.TryResolveAsync(BuildContext(BuildQuery(3, "intranet.corp.example.")));
        Assert.Equal(1, globalUpstream.QueryCount);
    }

    [Fact]
    public async Task Cache_OnAHit_EachClientsOwnBlockListStillApplies()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(BuildCloakedResponse());
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");

        // Only the 10.0.0.5 client's group blocks the tracker the answer's CNAME points at.
        await SeedScopedBlockListAsync("10.0.0.5/32", "tracker.example");

        await using CachingPipeline pipeline = await CachingPipeline.StartAsync(
            this,
            new DnsFilterOptions { Enable = true },
            rewriteEngine: null
        );

        DnsMessage unfilteredFirst = await pipeline.QueryAsync("10.0.0.6", "metrics.shop.example.");
        DnsMessage blockedFromCache = await pipeline.QueryAsync(
            "10.0.0.5",
            "metrics.shop.example."
        );
        DnsMessage unfilteredFromCache = await pipeline.QueryAsync(
            "10.0.0.6",
            "metrics.shop.example."
        );

        Assert.Equal(1, upstream.QueryCount);
        Assert.Equal(DnsResponseCodeEnum.NoError, unfilteredFirst.ResponseCode);
        Assert.Equal(2, unfilteredFirst.Answers.Count);
        Assert.Equal(DnsResponseCodeEnum.NxDomain, blockedFromCache.ResponseCode);
        Assert.Empty(blockedFromCache.Answers);
        Assert.Equal(DnsResponseCodeEnum.NoError, unfilteredFromCache.ResponseCode);
        Assert.Equal(2, unfilteredFromCache.Answers.Count);
    }

    [Fact]
    public async Task Cache_ABlockedClientsQuery_NeverPutsABlockResponseInTheCache()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(BuildCloakedResponse());
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        await SeedScopedBlockListAsync("10.0.0.5/32", "tracker.example");

        await using CachingPipeline pipeline = await CachingPipeline.StartAsync(
            this,
            new DnsFilterOptions { Enable = true },
            rewriteEngine: null
        );

        // The blocked client asks first, so the upstream answer is fetched on its behalf...
        DnsMessage blocked = await pipeline.QueryAsync("10.0.0.5", "metrics.shop.example.");
        // ...yet what was cached is the upstream answer, not the block.
        DnsMessage unfiltered = await pipeline.QueryAsync("10.0.0.6", "metrics.shop.example.");

        Assert.Equal(DnsResponseCodeEnum.NxDomain, blocked.ResponseCode);
        Assert.Equal(DnsResponseCodeEnum.NoError, unfiltered.ResponseCode);
        Assert.Equal(2, unfiltered.Answers.Count);
        Assert.Equal(1, upstream.QueryCount);
    }

    [Fact]
    public async Task Cache_OnAHit_AClientsRewriteStillWins()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");

        await using CachingPipeline pipeline = await CachingPipeline.StartAsync(
            this,
            new DnsFilterOptions { Enable = false },
            new SingleClientRewriteEngine(
                IPAddress.Parse("10.0.0.5"),
                IPAddress.Parse("192.168.1.50")
            )
        );

        DnsMessage cached = await pipeline.QueryAsync("10.0.0.6", "example.com.");
        DnsMessage rewritten = await pipeline.QueryAsync("10.0.0.5", "example.com.");
        DnsMessage fromCache = await pipeline.QueryAsync("10.0.0.6", "example.com.");

        Assert.Equal(1, upstream.QueryCount);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), FirstAnswerAddress(cached));
        Assert.Equal(IPAddress.Parse("192.168.1.50"), FirstAnswerAddress(rewritten));
        Assert.Equal(IPAddress.Parse("192.0.2.10"), FirstAnswerAddress(fromCache));
    }

    /// <summary>
    ///     This method seeds a non-default group, matched by one network, with one local domain
    ///     block list -- so only clients inside that network are filtered.
    /// </summary>
    private async Task SeedScopedBlockListAsync(string cidr, params string[] blockedDomains)
    {
        using IServiceScope scope = _provider.CreateScope();
        IServiceProvider services = scope.ServiceProvider;

        DnsClientGroupEntity group = new()
        {
            Id = Guid.NewGuid(),
            Name = "kids",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        DnsFilterListEntity list = new()
        {
            Id = Guid.NewGuid(),
            Name = "trackers",
            Kind = DnsFilterListKindEnum.Domain,
            Action = DnsFilterListActionEnum.Block,
            Source = DnsFilterListSourceEnum.Local,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await services.GetRequiredService<IDnsClientGroupRepository>().AddAsync(group);
        await services
            .GetRequiredService<IDnsClientGroupNetworkRepository>()
            .AddAsync(
                new DnsClientGroupNetworkEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = group.Id,
                    Cidr = cidr,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await services.GetRequiredService<IDnsFilterListRepository>().AddAsync(list);
        await services
            .GetRequiredService<IDnsClientGroupListAssignmentRepository>()
            .AddAsync(
                new DnsClientGroupListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = group.Id,
                    ListId = list.Id,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        foreach (string domain in blockedDomains)
            await services
                .GetRequiredService<IDnsFilterListEntryRepository>()
                .AddAsync(
                    new DnsFilterListEntryEntity
                    {
                        Id = Guid.NewGuid(),
                        ListId = list.Id,
                        Value = domain,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task DisableZoneAsync(string zoneName)
    {
        using IServiceScope scope = _provider.CreateScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        ZoneEntity zone = await repository.FindByNameAsync(zoneName);
        zone.IsDisabled = true;
        repository.Update(zone);

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This class is a running pipeline with one caching forwarder behind it, kept alive
    ///     across several queries (unlike the CNAME-cloaking tests' one-shot helper) so a later
    ///     query can be answered from what an earlier one cached.
    /// </summary>
    private sealed class CachingPipeline : IAsyncDisposable
    {
        private readonly DnsQueryProcessingPipeline _pipeline;
        private ushort _nextId = 100;

        private CachingPipeline(DnsQueryProcessingPipeline pipeline) => _pipeline = pipeline;

        public static async Task<CachingPipeline> StartAsync(
            ForwarderQueryResolverTests fixture,
            DnsFilterOptions filterOptions,
            IDnsRewriteEngine rewriteEngine
        )
        {
            DnsFilterEngine engine = new(
                fixture._provider.GetRequiredService<IServiceScopeFactory>(),
                new StaticOptionsMonitor<DnsFilterOptions>(filterOptions),
                NullLogger<DnsFilterEngine>.Instance
            );
            RecordingEventPublisher events = new();
            ForwarderQueryResolver resolver = fixture.CreateCachingResolver(
                fixture.CreateResponseCache()
            );

            DnsQueryProcessingPipeline pipeline = new(
                new DnsQueryDispatcher([resolver], events),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance,
                filterEngine: engine,
                rewriteEngine: rewriteEngine,
                eventPublisher: events
            );
            await pipeline.StartAsync(CancellationToken.None);

            return new CachingPipeline(pipeline);
        }

        public async Task<DnsMessage> QueryAsync(string clientAddress, string name)
        {
            TaskCompletionSource<byte[]> received = new();

            await _pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    DnsMessageWriter.Write(BuildQuery(_nextId++, name)),
                    DnsTransportProtocolEnum.Udp,
                    IPAddress.Parse(clientAddress),
                    (bytes, _) =>
                    {
                        received.TrySetResult(bytes.ToArray());
                        return ValueTask.CompletedTask;
                    }
                )
            );

            return DnsMessageReader.Read(await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        public async ValueTask DisposeAsync()
        {
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
        }
    }

    /// <summary>
    ///     This class stands in for the rewrite engine: it rewrites every A query from one
    ///     client to a fixed address and leaves everyone else alone, which is all it takes to
    ///     show the pipeline consults rewrites per client ahead of the cache.
    /// </summary>
    private sealed class SingleClientRewriteEngine(IPAddress client, IPAddress rewrittenTo)
        : IDnsRewriteEngine
    {
        public ValueTask<DnsMessage> TryBuildResponseAsync(
            DnsMessage request,
            IPAddress remoteAddress = null,
            CancellationToken stoppingToken = default
        )
        {
            if (!client.Equals(remoteAddress))
                return ValueTask.FromResult<DnsMessage>(null);

            DnsMessage response = new()
            {
                Id = request.Id,
                IsResponse = true,
                RecursionDesired = request.RecursionDesired,
                RecursionAvailable = true,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = [.. request.Questions],
            };
            response.Answers.Add(
                new DnsResourceRecord(
                    request.Questions[0].Name,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    60,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.A,
                        new ARecordData(rewrittenTo)
                    )
                )
            );

            return ValueTask.FromResult(response);
        }
    }
}
