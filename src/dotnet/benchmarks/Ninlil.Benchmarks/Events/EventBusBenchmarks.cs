using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Benchmarks.Dns;
using Ninlil.Benchmarks.Pipeline;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.ClientIdentity.Extension;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.QueryLog;
using Ninlil.Dns.Server.QueryLog.Extension;

// Define our namespace.
namespace Ninlil.Benchmarks.Events;

/// <summary>
///     This class measures the DNS event bus by itself: a real <c>DnsServerEventPublisher</c>
///     and <c>DnsServerEventDispatcher</c> with the three subscribers of
///     <c>DnsQueryAnsweredEvent</c> a running host has (the query-log subscriber, the client
///     presence subscriber and a counting subscriber that stands in for the SignalR
///     broadcaster). Each invocation publishes 10,000 events and waits until the counting
///     subscriber, which runs last, has seen every one of them, so the mean is the time per
///     delivered event and the allocation column the memory per event. The query-log queue is
///     emptied by a drain task, as in <see cref="PipelineBenchmarkHost" />, and the event queue
///     is large enough that nothing is dropped; the setup checks that one batch is delivered
///     whole and that nothing was dropped.
///     <para>
///         Every per-query subscriber is a singleton; the dispatcher still opens one dependency
///         injection scope per event.
///     </para>
/// </summary>
[Config(typeof(StorageConfig))]
public class EventBusBenchmarks
{
    private const int EventsPerInvoke = 10_000;

    private ServiceProvider _provider;
    private DnsServerEventPublisher _publisher;
    private CountingSubscriber _counter;
    private DnsQueryLogQueue _queue;
    private IHostedService _dispatcher;
    private readonly CancellationTokenSource _drainStopping = new();
    private Task _drain;
    private DnsQueryAnsweredEvent _event;

    /// <summary>
    ///     This method builds the bus, starts the dispatcher and the drain task and checks one
    ///     batch.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    // Room for a whole batch, so the publisher never drops an event here.
                    ["DnsServer:Events:Capacity"] = (EventsPerInvoke * 4).ToString(),
                    ["DnsServer:QueryLogging:Enable"] = "true",
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddNinlilDnsServerCore(configuration);
        services.AddNinlilDnsServerQueryLog(configuration);
        services.AddNinlilDnsServerClientIdentity(configuration);
        services.AddSingleton<IClientIdentityResolver, BenchmarkClientIdentityResolver>();
        services.AddSingleton<CountingSubscriber>();
        services.AddSingleton<IDnsServerEventSubscriber<DnsQueryAnsweredEvent>>(provider =>
            provider.GetRequiredService<CountingSubscriber>()
        );
        _provider = services.BuildServiceProvider();

        _publisher = _provider.GetRequiredService<DnsServerEventPublisher>();
        _counter = _provider.GetRequiredService<CountingSubscriber>();
        _queue = _provider.GetRequiredService<DnsQueryLogQueue>();
        _dispatcher = _provider
            .GetServices<IHostedService>()
            .Single(service => service is DnsServerEventDispatcher);
        await _dispatcher.StartAsync(CancellationToken.None);
        _drain = Task.Run(() => PipelineBenchmarkHost.DrainAsync(_queue, _drainStopping.Token));

        _event = new DnsQueryAnsweredEvent(
            DateTimeOffset.UtcNow,
            DnsTransportProtocolEnum.Udp,
            BenchmarkClientIdentityResolver.InTwoGroups,
            "www.example.org",
            DnsResourceRecordTypeEnum.A,
            DnsResponseCodeEnum.NoError,
            1
        );

        await PublishBatchAsync();

        if (_counter.Count != EventsPerInvoke || _queue.DroppedCount != 0)
            throw new InvalidOperationException(
                $"Benchmark setup check failed: one batch delivered {_counter.Count} of {EventsPerInvoke} events with {_queue.DroppedCount} query-log entries dropped."
            );
    }

    /// <summary>
    ///     This method stops the dispatcher and the drain task.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _dispatcher.StopAsync(CancellationToken.None);
        await _drainStopping.CancelAsync();
        await _drain;
        await _provider.DisposeAsync();
        _drainStopping.Dispose();
    }

    /// <summary>
    ///     This method publishes 10,000 answered-query events and waits until all are delivered.
    /// </summary>
    [Benchmark(OperationsPerInvoke = EventsPerInvoke)]
    public Task PublishAndDeliver() => PublishBatchAsync();

    private async Task PublishBatchAsync()
    {
        Task done = _counter.ExpectAsync(EventsPerInvoke);

        for (int index = 0; index < EventsPerInvoke; index++)
            _publisher.Publish(_event);

        await done;
    }

    /// <summary>
    ///     This class counts delivered events and completes a task when the expected number has
    ///     arrived. It is the last subscriber of <c>DnsQueryAnsweredEvent</c> in registration
    ///     order, so the dispatcher has called every other subscriber of an event when this one
    ///     sees it.
    /// </summary>
    private sealed class CountingSubscriber : IDnsServerEventSubscriber<DnsQueryAnsweredEvent>
    {
        private TaskCompletionSource _completion = new();
        private int _expected;
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task ExpectAsync(int expected)
        {
            // Asynchronous continuations keep the benchmark thread off the dispatcher's loop.
            _completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            Volatile.Write(ref _count, 0);
            Volatile.Write(ref _expected, expected);
            return _completion.Task;
        }

        public ValueTask HandleAsync(
            DnsQueryAnsweredEvent domainEvent,
            CancellationToken stoppingToken
        )
        {
            if (Interlocked.Increment(ref _count) == Volatile.Read(ref _expected))
                _completion.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }
}
