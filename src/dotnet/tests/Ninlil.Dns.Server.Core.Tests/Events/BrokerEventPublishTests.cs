using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.EventBus;

/// <summary>
///     This class tests that <see cref="DnsServerEventPublisher.Publish" /> hands broker
///     publishing to <see cref="BrokerEventPublishService" />: the caller never waits on the
///     transport, the transport sees one publish at a time, and a failing transport never
///     reaches the caller. It builds the publisher and the service by hand with a fake transport.
/// </summary>
public class BrokerEventPublishTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Publish_WhenTheTransportBlocksSynchronously_ReturnsAtOnce()
    {
        await using Harness harness = await Harness.StartAsync(
            new FakeTransport { BlockFor = TimeSpan.FromSeconds(2) }
        );

        // The first call warms up JIT and the pump; the measured calls follow.
        harness.Publisher.Publish(NewEvent());
        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int index = 0; index < 3; index++)
            harness.Publisher.Publish(NewEvent());

        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 100,
            $"Publish blocked the caller for {stopwatch.ElapsedMilliseconds} ms."
        );
    }

    [Fact]
    public async Task Publish_FromManyThreads_SendsOneEnvelopeAtATimeStampedWithTheInstanceId()
    {
        FakeTransport transport = new() { BlockFor = TimeSpan.FromMilliseconds(1) };
        await using Harness harness = await Harness.StartAsync(transport, capacity: 1000);

        Task[] threads = Enumerable
            .Range(0, 8)
            .Select(_ =>
                Task.Run(() =>
                {
                    for (int index = 0; index < 50; index++)
                        harness.Publisher.Publish(NewEvent());
                })
            )
            .ToArray();
        await Task.WhenAll(threads);
        await transport.WaitForCountAsync(400);

        Assert.Equal(1, transport.MaxConcurrency);
        Assert.Equal(400, transport.Count);
        Assert.All(
            transport.Envelopes,
            envelope => Assert.Equal(harness.Publisher.InstanceId, envelope.OriginInstanceId)
        );
    }

    [Fact]
    public async Task Publish_WhenTheTransportAlwaysThrows_NeverThrowsAndWarnsOnce()
    {
        FakeTransport transport = new() { Failure = new InvalidOperationException("broker down") };
        await using Harness harness = await Harness.StartAsync(transport, capacity: 1000);

        for (int index = 0; index < 100; index++)
            harness.Publisher.Publish(NewEvent());

        await transport.WaitForCountAsync(100);
        await Task.Delay(200);

        string[] warnings =
        [
            .. harness
                .PumpLogger.Entries.Concat(harness.PublisherLogger.Entries)
                .Where(entry => entry.Level == LogLevel.Warning)
                .Select(entry => entry.Message),
        ];
        Assert.Single(warnings);
    }

    [Fact]
    public async Task Publish_WhenTheBrokerQueueOverflows_KeepsLocalDeliveryAndWarnsWithTheCount()
    {
        TaskCompletionSource release = new();
        FakeTransport transport = new() { Gate = release.Task };
        await using Harness harness = await Harness.StartAsync(transport, capacity: 2);

        // The pump takes the first event and blocks in the transport; two fit the queue, the
        // remaining three displace older ones.
        harness.Publisher.Publish(NewEvent());
        await transport.WaitForEntryAsync();

        for (int index = 0; index < 5; index++)
            harness.Publisher.Publish(NewEvent());

        release.SetResult();
        await transport.WaitForCountAsync(3);
        await Task.Delay(200);

        Assert.Equal(3, transport.Count);
        Assert.Contains(
            harness.PublisherLogger.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("3")
                && entry.Message.Contains("message broker")
        );
    }

    private static DnsCacheEvictedEvent NewEvent() => new(DateTimeOffset.UtcNow, "test", 1);

    private sealed class FakeTransport : IDnsServerEventBrokerTransport
    {
        private readonly List<DnsServerEventEnvelope> _envelopes = [];
        private readonly TaskCompletionSource _entered = new();
        private int _concurrency;
        private int _maxConcurrency;

        public TimeSpan BlockFor { get; init; }
        public Exception Failure { get; init; }
        public Task Gate { get; init; }
        public bool IsConnected => true;
        public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

        public int Count
        {
            get
            {
                lock (_envelopes)
                    return _envelopes.Count;
            }
        }

        public DnsServerEventEnvelope[] Envelopes
        {
            get
            {
                lock (_envelopes)
                    return [.. _envelopes];
            }
        }

        public Task PublishAsync(DnsServerEventEnvelope envelope, CancellationToken stoppingToken)
        {
            int now = Interlocked.Increment(ref _concurrency);
            int seen;

            while (now > (seen = Volatile.Read(ref _maxConcurrency)))
                Interlocked.CompareExchange(ref _maxConcurrency, now, seen);

            _entered.TrySetResult();

            try
            {
                // A synchronous wait on purpose: it models a transport (the ActiveMQ client)
                // whose send blocks the calling thread.
                if (BlockFor > TimeSpan.Zero)
                    Thread.Sleep(BlockFor);

                Gate?.Wait(TimeSpan.FromSeconds(5));

                lock (_envelopes)
                    _envelopes.Add(envelope);

                if (Failure is not null)
                    throw Failure;

                return Task.CompletedTask;
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }

        public Task StartConsumingAsync(
            Func<DnsServerEventEnvelope, CancellationToken, Task> onEnvelopeReceived,
            CancellationToken stoppingToken
        ) => Task.CompletedTask;

        public Task WaitForEntryAsync() => _entered.Task.WaitAsync(Wait);

        public async Task WaitForCountAsync(int count)
        {
            DateTime deadline = DateTime.UtcNow + Wait;

            while (Count < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Only {Count} of {count} envelopes arrived.");

                await Task.Delay(10);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private BrokerEventPublishService _service;

        public DnsServerEventPublisher Publisher { get; private set; }
        public CapturingLogger<BrokerEventPublishService> PumpLogger { get; } = new();
        public CapturingLogger<DnsServerEventPublisher> PublisherLogger { get; } = new();

        public static async Task<Harness> StartAsync(FakeTransport transport, int capacity = 100)
        {
            IOptions<DnsServerEventPublisherOptions> options = Options.Create(
                new DnsServerEventPublisherOptions
                {
                    Capacity = capacity,
                    Transport = EventTransportEnum.RabbitMq,
                }
            );
            Harness harness = new();
            DnsServerEventPublisher publisher = new(options, harness.PublisherLogger, transport);
            harness.Publisher = publisher;
            harness._service = new BrokerEventPublishService(
                publisher,
                options,
                harness.PumpLogger,
                transport
            );
            await harness._service.StartAsync(CancellationToken.None);

            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            await _service.StopAsync(CancellationToken.None);
            _service.Dispose();
        }
    }
}
