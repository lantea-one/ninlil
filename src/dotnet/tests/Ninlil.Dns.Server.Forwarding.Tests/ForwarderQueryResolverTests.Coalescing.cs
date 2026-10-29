using System.Collections.Concurrent;
using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> tests that identical forwarded
///     queries in flight at once share one upstream query. It uses the real resolver, forwarder
///     set cache and response cache over in-memory SQLite, with a fake forwarder client
///     (<see cref="GatedUpstreamClient" />) in place of the network: the client counts calls and
///     holds chosen calls at a gate, so a test decides when the upstream answers and nothing
///     depends on timing. A test waits for a state (calls received, followers waiting) with
///     <see cref="UntilAsync" /> instead of sleeping.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    private static readonly TimeSpan _coalescingTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     This class is a forwarder client whose behaviour a test supplies per call number. It
    ///     counts every call, records the question and the flags of each outbound query, and
    ///     records whether the token of the first call was cancelled when the call ended.
    /// </summary>
    private sealed class GatedUpstreamClient(
        Func<int, DnsMessage, CancellationToken, Task<DnsMessage>> behaviour
    ) : IUpstreamForwarderClient
    {
        private int _callCount;

        /// <inheritdoc />
        public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Plain;

        /// <summary>This property contains how many queries this client has been given.</summary>
        public int CallCount => Volatile.Read(ref _callCount);

        /// <summary>This property contains every outbound query received, in arrival order.</summary>
        public ConcurrentQueue<DnsMessage> Queries { get; } = new();

        /// <inheritdoc />
        public Task<DnsMessage> ForwardAsync(
            UpstreamForwarderDefinition definition,
            DnsMessage query,
            CancellationToken stoppingToken = default
        )
        {
            Queries.Enqueue(query);

            return behaviour(Interlocked.Increment(ref _callCount), query, stoppingToken);
        }
    }

    /// <summary>
    ///     This method builds an upstream answer to <paramref name="query" /> with one A record
    ///     of the given TTL, carrying the query's ID.
    /// </summary>
    private static DnsMessage Reply(DnsMessage query, uint timeToLive = 300)
    {
        DnsMessage response = new()
        {
            Id = query.Id,
            IsResponse = true,
            RecursionDesired = true,
            RecursionAvailable = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };
        response.Questions.AddRange(query.Questions);
        response.Answers.Add(
            new DnsResourceRecord(
                query.Questions[0].Name,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                timeToLive,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(IPAddress.Parse("192.0.2.10"))
                )
            )
        );

        return response;
    }

    /// <summary>
    ///     This method waits until <paramref name="condition" /> holds, polling without a fixed
    ///     delay, and fails the test if it does not within <see cref="_coalescingTimeout" />.
    /// </summary>
    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        using CancellationTokenSource timeout = new(_coalescingTimeout);

        while (!condition())
        {
            if (timeout.IsCancellationRequested)
                Assert.Fail($"Timed out waiting for: {what}.");

            await Task.Yield();
        }
    }

    private ForwarderQueryResolver CreateCoalescingResolver(
        GatedUpstreamClient client,
        IForwardedResponseCache cache = null,
        IDnssecValidator validator = null,
        RecursiveServerOptions recursiveOptions = null,
        EdnsClientSubnetService clientSubnetService = null
    ) =>
        CreateResolver(
            forwardingOptions: _cachingForwardingOptions,
            recursiveOptions: recursiveOptions ?? _cachingRecursiveOptions,
            validator: validator,
            responseCache: cache ?? CreateResponseCache(),
            forwarderClients: [client],
            clientSubnetService: clientSubnetService
        );

    /// <summary>
    ///     This method starts a query on the thread pool so several can be in flight at once.
    /// </summary>
    private static Task<DnsQueryResolution> StartQuery(
        ForwarderQueryResolver resolver,
        DnsMessage request,
        CancellationToken stoppingToken = default,
        IPAddress remote = null
    ) =>
        Task.Run(
            () =>
                resolver
                    .TryResolveAsync(
                        new DnsQueryContext(
                            request,
                            DnsTransportProtocolEnum.Udp,
                            remote ?? IPAddress.Loopback,
                            DateTimeOffset.UtcNow
                        ),
                        stoppingToken
                    )
                    .AsTask(),
            CancellationToken.None
        );

    private static DnsMessage EdnsQuery(
        ushort id,
        string name,
        DnsResourceRecordTypeEnum type = DnsResourceRecordTypeEnum.A,
        DnsClassEnum recordClass = DnsClassEnum.Internet,
        bool dnssecOk = false,
        bool checkingDisabled = false,
        bool edns = true
    ) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            CheckingDisabled = checkingDisabled,
            Questions = { new DnsQuestion(DnsName.Parse(name), type, recordClass) },
            Edns = edns ? new DnsEdnsOptions(0, 1232, dnssecOk, []) : null,
        };

    private static void AssertAnsweredWithOwnId(DnsQueryResolution resolution, ushort id)
    {
        Assert.True(resolution.IsHandled);
        Assert.Equal(id, resolution.Response.Id);
        Assert.Single(resolution.Response.Answers);
    }

    [Fact]
    public async Task Coalescing_ABurstOfIdenticalQueries_SendsOneUpstreamQueryAndEachReplyKeepsItsOwnId()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedUpstreamClient upstream = new(
            async (call, query, token) =>
            {
                await gate.Task.WaitAsync(token);

                return Reply(query);
            }
        );
        await SeedForwarderAsync("127.0.0.1:5301");
        ForwarderQueryResolver resolver = CreateCoalescingResolver(upstream);

        Task<DnsQueryResolution> leader = StartQuery(resolver, BuildQuery(1, "example.com."));
        await UntilAsync(() => upstream.CallCount == 1, "the leader's upstream query");

        // The followers differ in ID and in the case of the name (0x20), which the key ignores.
        List<Task<DnsQueryResolution>> followers =
        [
            .. Enumerable
                .Range(2, 49)
                .Select(id =>
                    StartQuery(
                        resolver,
                        BuildQuery((ushort)id, id % 2 == 0 ? "EXAMPLE.com." : "Example.COM.")
                    )
                ),
        ];
        await UntilAsync(() => resolver.FollowersWaiting == 49, "49 followers waiting");

        Assert.Equal(1, resolver.InFlightCount);
        Assert.Equal(1, upstream.CallCount);

        gate.SetResult();
        DnsQueryResolution[] all = await Task.WhenAll([leader, .. followers]);

        Assert.Equal(1, upstream.CallCount);

        for (int index = 0; index < all.Length; index++)
            AssertAnsweredWithOwnId(all[index], (ushort)(index + 1));

        Assert.Equal(0, resolver.InFlightCount);
        Assert.Equal(0, resolver.FollowersWaiting);

        // The map keeps nothing for the finished key: once the cached answer expires the next
        // query becomes a leader again.
        _cacheClock.Advance(TimeSpan.FromSeconds(301));
        DnsQueryResolution later = await StartQuery(resolver, BuildQuery(99, "example.com."));

        AssertAnsweredWithOwnId(later, 99);
        Assert.Equal(2, upstream.CallCount);
        Assert.Equal(0, resolver.InFlightCount);
    }

    [Fact]
    public async Task Coalescing_AFollowerWhoseOwnTokenIsCancelled_StopsWaitingAndLeavesTheLeaderRunning()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool leaderTokenCancelled = true;
        GatedUpstreamClient upstream = new(
            async (call, query, token) =>
            {
                await gate.Task.WaitAsync(token);
                leaderTokenCancelled = token.IsCancellationRequested;

                return Reply(query);
            }
        );
        await SeedForwarderAsync("127.0.0.1:5301");
        ForwarderQueryResolver resolver = CreateCoalescingResolver(upstream);

        Task<DnsQueryResolution> leader = StartQuery(resolver, BuildQuery(1, "example.com."));
        await UntilAsync(() => upstream.CallCount == 1, "the leader's upstream query");

        using CancellationTokenSource followerCancel = new();
        Task<DnsQueryResolution> cancelled = StartQuery(
            resolver,
            BuildQuery(2, "example.com."),
            followerCancel.Token
        );
        Task<DnsQueryResolution> patient = StartQuery(resolver, BuildQuery(3, "example.com."));
        await UntilAsync(() => resolver.FollowersWaiting == 2, "two followers waiting");

        await followerCancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await UntilAsync(() => resolver.FollowersWaiting == 1, "the cancelled follower gone");

        // The leader has not been touched and the other follower still waits.
        Assert.False(leader.IsCompleted);
        Assert.False(patient.IsCompleted);
        Assert.Equal(1, resolver.InFlightCount);

        gate.SetResult();
        AssertAnsweredWithOwnId(await leader, 1);
        AssertAnsweredWithOwnId(await patient, 3);

        Assert.False(leaderTokenCancelled);
        Assert.Equal(1, upstream.CallCount);
        Assert.Equal(0, resolver.InFlightCount);
        Assert.Equal(0, resolver.FollowersWaiting);
    }

    [Fact]
    public async Task Coalescing_WhenTheLeaderIsCancelled_EveryFollowerRunsItsOwnAttempt()
    {
        TaskCompletionSource leaderGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource followerGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedUpstreamClient upstream = new(
            async (call, query, token) =>
            {
                // Call 1 is the leader's and only ends by cancellation. Every later call is a
                // follower's own attempt, held until the test has seen all of them arrive.
                await (call == 1 ? leaderGate : followerGate).Task.WaitAsync(token);

                return Reply(query);
            }
        );
        await SeedForwarderAsync("127.0.0.1:5301");
        ForwarderQueryResolver resolver = CreateCoalescingResolver(upstream);

        using CancellationTokenSource leaderCancel = new();
        Task<DnsQueryResolution> leader = StartQuery(
            resolver,
            BuildQuery(1, "example.com."),
            leaderCancel.Token
        );
        await UntilAsync(() => upstream.CallCount == 1, "the leader's upstream query");

        List<Task<DnsQueryResolution>> followers =
        [
            .. Enumerable
                .Range(2, 3)
                .Select(id => StartQuery(resolver, BuildQuery((ushort)id, "example.com."))),
        ];
        await UntilAsync(() => resolver.FollowersWaiting == 3, "three followers waiting");

        await leaderCancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leader);

        // None of the followers is faulted or answered from the leader's end: each sends its
        // own query, so the upstream sees 1 + 3 calls.
        await UntilAsync(() => upstream.CallCount == 4, "three follower attempts");
        Assert.Equal(0, resolver.InFlightCount);
        Assert.All(followers, follower => Assert.False(follower.IsCompleted));

        followerGate.SetResult();
        DnsQueryResolution[] results = await Task.WhenAll(followers);

        for (int index = 0; index < results.Length; index++)
            AssertAnsweredWithOwnId(results[index], (ushort)(index + 2));

        Assert.Equal(4, upstream.CallCount);
        Assert.Equal(0, resolver.InFlightCount);
    }

    [Fact]
    public async Task Coalescing_WhenTheLeaderFailsOnEveryForwarder_EachFollowerRunsItsOwnAttemptOnEveryForwarder()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedUpstreamClient upstream = new(
            async (call, query, token) =>
            {
                if (call == 1)
                    await gate.Task.WaitAsync(token);

                throw new IOException("The fake upstream is down.");
            }
        );
        await SeedForwarderAsync("127.0.0.1:5301", sortOrder: 0);
        await SeedForwarderAsync("127.0.0.1:5302", sortOrder: 1);
        ForwarderQueryResolver resolver = CreateCoalescingResolver(upstream);

        Task<DnsQueryResolution> leader = StartQuery(resolver, BuildQuery(1, "example.com."));
        await UntilAsync(() => upstream.CallCount == 1, "the leader's upstream query");

        List<Task<DnsQueryResolution>> followers =
        [
            .. Enumerable
                .Range(2, 3)
                .Select(id => StartQuery(resolver, BuildQuery((ushort)id, "example.com."))),
        ];
        await UntilAsync(() => resolver.FollowersWaiting == 3, "three followers waiting");

        gate.SetResult();
        DnsQueryResolution[] results = await Task.WhenAll([leader, .. followers]);

        // Four queries, two forwarders each: exactly what four independent queries send.
        Assert.Equal(8, upstream.CallCount);
        Assert.All(results, result => Assert.False(result.IsHandled));
        Assert.Equal(0, resolver.InFlightCount);
        Assert.Equal(0, resolver.FollowersWaiting);
    }

    [Fact]
    public async Task Coalescing_WhenTheLeadersAnswerIsNotCacheable_EachFollowerForwardsItself()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedUpstreamClient upstream = new(
            async (call, query, token) =>
            {
                if (call == 1)
                    await gate.Task.WaitAsync(token);

                return Reply(query, timeToLive: 0);
            }
        );
        await SeedForwarderAsync("127.0.0.1:5301");
        ForwardedResponseCache cache = CreateResponseCache();
        ForwarderQueryResolver resolver = CreateCoalescingResolver(upstream, cache);

        Task<DnsQueryResolution> leader = StartQuery(resolver, BuildQuery(1, "example.com."));
        await UntilAsync(() => upstream.CallCount == 1, "the leader's upstream query");

        List<Task<DnsQueryResolution>> followers =
        [
            .. Enumerable
                .Range(2, 3)
                .Select(id => StartQuery(resolver, BuildQuery((ushort)id, "example.com."))),
        ];
        await UntilAsync(() => resolver.FollowersWaiting == 3, "three followers waiting");

        gate.SetResult();
        DnsQueryResolution[] results = await Task.WhenAll([leader, .. followers]);

        Assert.Equal(4, upstream.CallCount);

        for (int index = 0; index < results.Length; index++)
            AssertAnsweredWithOwnId(results[index], (ushort)(index + 1));

        Assert.Equal(0, cache.GetStatistics().Hits);
        Assert.Equal(0, resolver.InFlightCount);
    }

    [Fact]
    public async Task Coalescing_WhenTheLeadersAnswerIsBogusForAClientThatSetCd_EachFollowerValidatesItsOwnAnswer()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedUpstreamClient upstream = new(
            async (call, query, token) =>
            {
                if (call == 1)
                    await gate.Task.WaitAsync(token);

                return Reply(query);
            }
        );
        await SeedForwarderAsync("127.0.0.1:5301");
        ForwarderQueryResolver resolver = CreateCoalescingResolver(
            upstream,
            validator: new BogusValidatorStub(),
            recursiveOptions: new RecursiveServerOptions
            {
                EnableDnssecValidation = true,
                EnableRecursion = true,
            }
        );

        Task<DnsQueryResolution> leader = StartQuery(
            resolver,
            EdnsQuery(1, "example.com.", checkingDisabled: true)
        );
        await UntilAsync(() => upstream.CallCount == 1, "the leader's upstream query");

        List<Task<DnsQueryResolution>> followers =
        [
            .. Enumerable
                .Range(2, 3)
                .Select(id =>
                    StartQuery(
                        resolver,
                        EdnsQuery((ushort)id, "example.com.", checkingDisabled: true)
                    )
                ),
        ];
        await UntilAsync(() => resolver.FollowersWaiting == 3, "three followers waiting");

        gate.SetResult();
        DnsQueryResolution[] results = await Task.WhenAll([leader, .. followers]);

        // A Bogus answer for a client with CD is returned without AD and never cached, so the
        // followers find nothing to share and each asks the upstream itself.
        Assert.Equal(4, upstream.CallCount);

        for (int index = 0; index < results.Length; index++)
        {
            AssertAnsweredWithOwnId(results[index], (ushort)(index + 1));
            Assert.False(results[index].Response.AuthenticData);
        }

        Assert.Equal(0, resolver.InFlightCount);
    }

    /// <summary>
    ///     This class is a validator that calls every answer Bogus.
    /// </summary>
    private sealed class BogusValidatorStub : IDnssecValidator
    {
        /// <inheritdoc />
        public Task<DnssecValidationResult> ValidateAsync(
            DnsName queriedName,
            DnsResourceRecordTypeEnum queriedType,
            DnsClassEnum recordClass,
            IterativeResolutionResult result,
            QueryBudget budget,
            CancellationToken stoppingToken = default,
            IDnssecRecordFetcher fetcher = null
        ) => Task.FromResult(DnssecValidationResult.Bogus("test"));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("type")]
    [InlineData("class")]
    [InlineData("do")]
    [InlineData("cd")]
    [InlineData("noedns")]
    [InlineData("ecs")]
    [InlineData("forwarderset")]
    public async Task Coalescing_AQueryThatDiffersInAnythingTheKeyHolds_GoesUpstreamWithoutWaiting(
        string difference
    )
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedUpstreamClient upstream = new(
            async (call, query, token) =>
            {
                // Only the first query is held; a query that went upstream on its own is not.
                if (call == 1)
                    await gate.Task.WaitAsync(token);

                return Reply(query);
            }
        );
        await SeedForwarderAsync("127.0.0.1:5301");
        ForwarderQueryResolver resolver = CreateCoalescingResolver(
            upstream,
            clientSubnetService: EnabledClientSubnetService()
        );
        IPAddress baseClient = IPAddress.Parse("192.0.2.5");

        Task<DnsQueryResolution> leader = StartQuery(
            resolver,
            EdnsQuery(1, "example.com."),
            remote: baseClient
        );
        await UntilAsync(() => upstream.CallCount == 1, "the leader's upstream query");

        DnsMessage other = difference switch
        {
            "name" => EdnsQuery(2, "other.example.com."),
            "type" => EdnsQuery(2, "example.com.", DnsResourceRecordTypeEnum.Aaaa),
            "class" => EdnsQuery(2, "example.com.", recordClass: DnsClassEnum.Chaos),
            "do" => EdnsQuery(2, "example.com.", dnssecOk: true),
            "cd" => EdnsQuery(2, "example.com.", checkingDisabled: true),
            "noedns" => EdnsQuery(2, "example.com.", edns: false),
            _ => EdnsQuery(2, "example.com."),
        };
        IPAddress otherClient = difference == "ecs" ? IPAddress.Parse("198.51.100.5") : baseClient;

        if (difference == "forwarderset")
        {
            // The same question now resolves to a forwarder zone's servers instead of the global
            // set, which the key must tell apart.
            await SeedForwarderZoneAsync("example.com", "127.0.0.1:5302");
        }

        Task<DnsQueryResolution> second = StartQuery(resolver, other, remote: otherClient);

        // The second query reaches the upstream while the first is still held, so it did not wait.
        await UntilAsync(
            () => upstream.CallCount == 2,
            $"a separate upstream query for a different {difference}"
        );
        Assert.Equal(0, resolver.FollowersWaiting);

        gate.SetResult();
        AssertAnsweredWithOwnId(await leader, 1);
        AssertAnsweredWithOwnId(await second, 2);

        Assert.Equal(2, upstream.CallCount);
        Assert.Equal(0, resolver.InFlightCount);
    }

    [Fact]
    public async Task Coalescing_WhenTheLeaderThrows_NoEntryIsLeftAndEachFollowerRunsItsOwnAttempt()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedUpstreamClient upstream = new(
            async (call, query, token) =>
            {
                if (call == 1)
                    await gate.Task.WaitAsync(token);

                return Reply(query);
            }
        );
        await SeedForwarderAsync("127.0.0.1:5301");
        ForwarderQueryResolver resolver = CreateCoalescingResolver(
            upstream,
            new ThrowOnFirstStoreCache(CreateResponseCache())
        );

        Task<DnsQueryResolution> leader = StartQuery(resolver, BuildQuery(1, "example.com."));
        await UntilAsync(() => upstream.CallCount == 1, "the leader's upstream query");

        List<Task<DnsQueryResolution>> followers =
        [
            .. Enumerable
                .Range(2, 2)
                .Select(id => StartQuery(resolver, BuildQuery((ushort)id, "example.com."))),
        ];
        await UntilAsync(() => resolver.FollowersWaiting == 2, "two followers waiting");

        gate.SetResult();

        // The leader's store throws after the upstream answered; the exception reaches the
        // caller, the entry is removed and the followers are released to ask for themselves.
        await Assert.ThrowsAsync<InvalidOperationException>(() => leader);
        DnsQueryResolution[] results = await Task.WhenAll(followers);

        AssertAnsweredWithOwnId(results[0], 2);
        AssertAnsweredWithOwnId(results[1], 3);
        Assert.Equal(0, resolver.InFlightCount);
        Assert.Equal(0, resolver.FollowersWaiting);
    }

    /// <summary>
    ///     This class is a response cache that throws from its first <c>TryStore</c> call and
    ///     otherwise passes everything to the real cache it wraps.
    /// </summary>
    private sealed class ThrowOnFirstStoreCache(IForwardedResponseCache inner)
        : IForwardedResponseCache
    {
        private int _stores;

        /// <inheritdoc />
        public DnsMessage TryGet(ForwardedResponseCacheKey key, DnsMessage request) =>
            inner.TryGet(key, request);

        /// <inheritdoc />
        public DnsMessage TryGetStale(ForwardedResponseCacheKey key, DnsMessage request) =>
            inner.TryGetStale(key, request);

        /// <inheritdoc />
        public bool TryStore(
            ForwardedResponseCacheKey key,
            DnsMessage response,
            bool isSecure = false
        ) =>
            Interlocked.Increment(ref _stores) == 1
                ? throw new InvalidOperationException("The fake cache cannot store.")
                : inner.TryStore(key, response, isSecure);

        /// <inheritdoc />
        public int Flush() => inner.Flush();
    }

    [Fact]
    public async Task Coalescing_WithTheCacheSwitchedOff_EveryIdenticalQueryGoesUpstreamAtOnce()
    {
        _cachingForwardingOptions.EnableCache = false;
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedUpstreamClient upstream = new(
            async (call, query, token) =>
            {
                await gate.Task.WaitAsync(token);

                return Reply(query);
            }
        );
        await SeedForwarderAsync("127.0.0.1:5301");
        ForwarderQueryResolver resolver = CreateCoalescingResolver(upstream);

        List<Task<DnsQueryResolution>> queries =
        [
            .. Enumerable
                .Range(1, 5)
                .Select(id => StartQuery(resolver, BuildQuery((ushort)id, "example.com."))),
        ];

        // With nothing stored a follower would have nothing to be answered from, so none waits.
        await UntilAsync(() => upstream.CallCount == 5, "five upstream queries");
        Assert.Equal(0, resolver.FollowersWaiting);
        Assert.Equal(0, resolver.InFlightCount);

        gate.SetResult();
        DnsQueryResolution[] results = await Task.WhenAll(queries);

        for (int index = 0; index < results.Length; index++)
            AssertAnsweredWithOwnId(results[index], (ushort)(index + 1));
    }

    [Fact]
    public async Task Coalescing_AForwarderSetWithNothingSendable_IsNotHandledEvenWithACachedAnswer()
    {
        GatedUpstreamClient upstream = new((call, query, token) => Task.FromResult(Reply(query)));

        // A host name whose lookup never finishes (the fake host resolver) has no address.
        const string unresolved = "udp://nohost.example:5353";
        await SeedForwarderAsync(unresolved);
        ForwardedResponseCache cache = CreateResponseCache();
        DnsMessage request = BuildQuery(1, "example.com.");
        cache.TryStore(
            ForwardedResponseCacheKey.Create(request, request, [unresolved], validated: false),
            Reply(request)
        );
        ForwarderQueryResolver resolver = CreateCoalescingResolver(upstream, cache);

        DnsQueryResolution resolution = await resolver.TryResolveAsync(BuildContext(request));

        Assert.False(resolution.IsHandled);
        Assert.Equal(0, upstream.CallCount);
        Assert.Equal(0, resolver.InFlightCount);
    }
}
