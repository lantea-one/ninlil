using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests that <see cref="DnsQueryProcessingPipeline" /> answers up to
///     <see cref="DnsQueryProcessingPipelineOptions.MaxConcurrentQueries" /> queries at once
///     whatever the worker count, and that shutdown and failures keep the "exactly one reply or
///     drop" rule under concurrency. It runs in memory with a resolver that blocks chosen query
///     IDs on gates the test opens, so no test depends on a delay: a test waits for an event
///     (a query started, a reply arrived) and only then moves on.
/// </summary>
public sealed class DnsQueryProcessingPipelineConcurrencyTests
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("203.0.113.78");

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This resolver blocks the query IDs a test registered with <see cref="Gate" /> until the
    ///     test opens the gate, and answers every other ID at once. It records which IDs have
    ///     started and the most queries it ever ran at the same time.
    /// </summary>
    private sealed class GatedResolver(bool honorCancellation = true) : IDnsQueryResolver
    {
        private readonly ConcurrentDictionary<ushort, TaskCompletionSource> _gates = new();
        private readonly ConcurrentDictionary<ushort, TaskCompletionSource> _started = new();
        private readonly TaskCompletionSource _cancelObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _current;
        private int _peak;

        public int Peak => Volatile.Read(ref _peak);

        /// <summary>
        ///     This property completes when a blocked query sees its cancellation token fire.
        /// </summary>
        public Task CancelObserved => _cancelObserved.Task;

        public TaskCompletionSource Gate(ushort id) =>
            _gates.GetOrAdd(
                id,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            );

        public Task Started(ushort id) =>
            _started
                .GetOrAdd(
                    id,
                    _ => new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    )
                )
                .Task.WaitAsync(WaitLimit);

        public async ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            ushort id = context.Request.Id;
            int current = Interlocked.Increment(ref _current);
            int peak;
            do
            {
                peak = Volatile.Read(ref _peak);
            } while (
                current > peak && Interlocked.CompareExchange(ref _peak, current, peak) != peak
            );

            _started
                .GetOrAdd(
                    id,
                    _ => new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    )
                )
                .TrySetResult();

            try
            {
                if (_gates.TryGetValue(id, out TaskCompletionSource gate))
                {
                    using CancellationTokenRegistration registration = stoppingToken.Register(() =>
                        _cancelObserved.TrySetResult()
                    );
                    await gate.Task.WaitAsync(
                        honorCancellation ? stoppingToken : CancellationToken.None
                    );
                }

                return DnsQueryResolution.Handled(
                    new DnsMessage
                    {
                        Id = id,
                        IsResponse = true,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                        Questions = [.. context.Request.Questions],
                    }
                );
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private sealed class ThrowingRateLimiter : IDnsRateLimiter
    {
        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => throw new InvalidOperationException("limiter failure");

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(DnsSubnetRateLimitDecisionEnum.Allow);
    }

    private sealed class NoOpPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    /// <summary>
    ///     This class records what the pipeline did with one message. <see cref="Settled" />
    ///     completes on the first reply or drop; the counts show whether it was exactly one.
    /// </summary>
    private sealed class Outcome(bool throwOnDrop = false)
    {
        private readonly TaskCompletionSource _settled = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _responses;
        private int _drops;
        private DnsQueryDropReasonEnum _reason;

        public int Responses => Volatile.Read(ref _responses);

        public int Drops => Volatile.Read(ref _drops);

        public DnsQueryDropReasonEnum Reason => _reason;

        public bool IsSettled => _settled.Task.IsCompleted;

        public Task Settled => _settled.Task.WaitAsync(WaitLimit);

        public ValueTask Respond(ReadOnlyMemory<byte> bytes, CancellationToken _)
        {
            Interlocked.Increment(ref _responses);
            _settled.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public void Drop(DnsQueryDropReasonEnum reason)
        {
            _reason = reason;
            Interlocked.Increment(ref _drops);
            _settled.TrySetResult();

            if (throwOnDrop)
                throw new InvalidOperationException("notifier failure");
        }
    }

    private static DnsQueryProcessingPipeline CreatePipeline(
        IDnsQueryResolver resolver,
        int workerCount,
        int maxConcurrentQueries,
        IDnsRateLimiter rateLimiter = null,
        int channelCapacity = 64
    ) =>
        new(
            new DnsQueryDispatcher([resolver], new NoOpPublisher()),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions
                {
                    WorkerCount = workerCount,
                    MaxConcurrentQueries = maxConcurrentQueries,
                    ChannelCapacity = channelCapacity,
                }
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            rateLimiter: rateLimiter
        );

    private static async Task<Outcome> SendAsync(
        DnsQueryProcessingPipeline pipeline,
        ushort id,
        bool throwOnDrop = false
    )
    {
        Outcome outcome = new(throwOnDrop);
        byte[] bytes = DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = id,
                RecursionDesired = true,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse("example.com."),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet
                    ),
                },
            }
        );

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                bytes,
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                outcome.Respond
            )
            {
                OnDropped = outcome.Drop,
            }
        );
        return outcome;
    }

    [Fact]
    public async Task OneSlowQueryDoesNotBlockOthersWhenThereIsASingleWorker()
    {
        GatedResolver resolver = new();
        TaskCompletionSource slowGate = resolver.Gate(1);
        DnsQueryProcessingPipeline pipeline = CreatePipeline(resolver, 1, 1000);
        await pipeline.StartAsync(CancellationToken.None);

        Outcome slow = await SendAsync(pipeline, 1);
        await resolver.Started(1);

        // The only worker's query is blocked, yet the next one is answered.
        Outcome fast = await SendAsync(pipeline, 2);
        await fast.Settled;

        Assert.Equal(1, fast.Responses);
        Assert.False(slow.IsSettled);

        slowGate.SetResult();
        await slow.Settled;
        Assert.Equal(1, slow.Responses);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task NoMoreThanMaxConcurrentQueriesRunAtOnceAndTheRestWaitForASlot()
    {
        GatedResolver resolver = new();
        TaskCompletionSource gate1 = resolver.Gate(1);
        TaskCompletionSource gate2 = resolver.Gate(2);
        TaskCompletionSource gate3 = resolver.Gate(3);
        TaskCompletionSource gate4 = resolver.Gate(4);

        // Four workers could start four queries; the cap of two must still hold.
        DnsQueryProcessingPipeline pipeline = CreatePipeline(resolver, 4, 2);
        await pipeline.StartAsync(CancellationToken.None);

        Outcome q1 = await SendAsync(pipeline, 1);
        Outcome q2 = await SendAsync(pipeline, 2);
        Outcome q3 = await SendAsync(pipeline, 3);
        Outcome q4 = await SendAsync(pipeline, 4);

        await resolver.Started(1);
        await resolver.Started(2);
        Assert.Equal(2, resolver.Peak);

        // Finishing one query frees a slot, which the next waiting query takes.
        gate1.SetResult();
        await q1.Settled;
        await resolver.Started(3);

        gate2.SetResult();
        await q2.Settled;
        await resolver.Started(4);

        gate3.SetResult();
        gate4.SetResult();
        await q3.Settled;
        await q4.Settled;

        Assert.Equal(2, resolver.Peak);
        Assert.All([q1, q2, q3, q4], outcome => Assert.Equal(1, outcome.Responses));

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task AFullPipelineLeavesWaitingQueriesInTheQueueSoTheQueueBoundsThem()
    {
        GatedResolver resolver = new();
        TaskCompletionSource gate = resolver.Gate(1);
        DnsQueryProcessingPipeline pipeline = CreatePipeline(resolver, 1, 1, channelCapacity: 1);
        await pipeline.StartAsync(CancellationToken.None);

        Outcome running = await SendAsync(pipeline, 1);
        await resolver.Started(1);

        // The queue holds query 2. The worker has no free slot, so it has not taken it, and the
        // enqueue of query 3 has to wait. This is the backpressure transports see.
        Outcome queued = await SendAsync(pipeline, 2);
        Task<Outcome> blockedEnqueue = SendAsync(pipeline, 3);
        Assert.False(blockedEnqueue.IsCompleted);

        gate.SetResult();
        await running.Settled;
        await queued.Settled;
        Outcome third = await blockedEnqueue.WaitAsync(WaitLimit);
        await third.Settled;

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task StopAsyncWaitsForAQueryThatIgnoresCancellationAndThenDelivers()
    {
        GatedResolver resolver = new(honorCancellation: false);
        TaskCompletionSource gate = resolver.Gate(1);
        DnsQueryProcessingPipeline pipeline = CreatePipeline(resolver, 1, 10);
        await pipeline.StartAsync(CancellationToken.None);

        Outcome outcome = await SendAsync(pipeline, 1);
        await resolver.Started(1);

        Task stopTask = pipeline.StopAsync(CancellationToken.None);

        // The resolver sees the token fire, but it is still blocked on its gate, so StopAsync
        // has to be waiting for it.
        await resolver.CancelObserved.WaitAsync(WaitLimit);
        Assert.False(stopTask.IsCompleted);

        gate.SetResult();
        await stopTask.WaitAsync(WaitLimit);

        // The query finished before StopAsync returned, and it was reported exactly once.
        Assert.Equal(1, outcome.Responses + outcome.Drops);

        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task ShutdownReportsFailedForEveryQueryInProgressAndEveryQueryStillQueued()
    {
        GatedResolver resolver = new();
        resolver.Gate(1);
        resolver.Gate(2);
        DnsQueryProcessingPipeline pipeline = CreatePipeline(resolver, 2, 2);
        await pipeline.StartAsync(CancellationToken.None);

        Outcome running1 = await SendAsync(pipeline, 1);
        Outcome running2 = await SendAsync(pipeline, 2);
        await resolver.Started(1);
        await resolver.Started(2);

        // Both slots are busy, so these two stay in the queue.
        Outcome queued3 = await SendAsync(pipeline, 3);
        Outcome queued4 = await SendAsync(pipeline, 4);

        await pipeline.StopAsync(CancellationToken.None);

        foreach (Outcome outcome in new[] { running1, running2, queued3, queued4 })
        {
            Assert.Equal(0, outcome.Responses);
            Assert.Equal(1, outcome.Drops);
            Assert.Equal(DnsQueryDropReasonEnum.Failed, outcome.Reason);
        }

        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task AThrowingDropNotifierDoesNotStopOtherQueries()
    {
        DnsQueryProcessingPipeline pipeline = CreatePipeline(
            new GatedResolver(),
            1,
            10,
            new ThrowingRateLimiter()
        );
        await pipeline.StartAsync(CancellationToken.None);

        // The limiter throws, the pipeline reports Failed, and the notifier throws in turn.
        Outcome first = await SendAsync(pipeline, 1, throwOnDrop: true);
        Outcome second = await SendAsync(pipeline, 2, throwOnDrop: true);
        await first.Settled;
        await second.Settled;

        Assert.Equal(1, first.Drops);
        Assert.Equal(DnsQueryDropReasonEnum.Failed, first.Reason);
        Assert.Equal(1, second.Drops);

        // StopAsync waits for every query task, so it would hang or throw if one had faulted.
        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task AThrowingResolverReportsFailedOnceAndFreesItsSlot()
    {
        // With a single slot, a query that threw and kept its slot would block the next one.
        GatedResolver good = new();
        ThrowOnceResolver resolver = new(good);
        DnsQueryProcessingPipeline pipeline = CreatePipeline(resolver, 1, 1);
        await pipeline.StartAsync(CancellationToken.None);

        Outcome failing = await SendAsync(pipeline, 1);
        await failing.Settled;
        Outcome next = await SendAsync(pipeline, 2);
        await next.Settled;

        Assert.Equal(1, failing.Drops);
        Assert.Equal(0, failing.Responses);
        Assert.Equal(DnsQueryDropReasonEnum.Failed, failing.Reason);
        Assert.Equal(1, next.Responses);

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    private sealed class ThrowOnceResolver(IDnsQueryResolver inner) : IDnsQueryResolver
    {
        private int _calls;

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) =>
            Interlocked.Increment(ref _calls) == 1
                ? throw new InvalidOperationException("resolver failure")
                : inner.TryResolveAsync(context, stoppingToken);
    }
}
