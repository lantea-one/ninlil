using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
///     This class tests the event bus end to end without a host: a real
///     <see cref="DnsServerEventPublisher" />, <see cref="DnsServerEventDispatcher" /> and
///     <see cref="BrokerEventConsumerService" /> built over a <see cref="ServiceCollection" />,
///     with a fake broker transport that records envelopes and lets a test push envelopes into the
///     consumer callback.
/// </summary>
public class DnsServerEventBusTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Publish_WithABrokerTransport_DeliversLocallyExactlyOnce()
    {
        await using Bus bus = await Bus.StartAsync(broker: true);

        bus.Publisher.Publish(
            new DhcpLeaseChangedEvent(
                DateTimeOffset.UtcNow,
                Guid.NewGuid(),
                DhcpLeaseChangeKindEnum.Granted
            )
        );
        await bus.DrainAsync();

        Assert.Single(bus.Local.Leases);
        Assert.Single(bus.Transport.PublishedExceptMarker);
    }

    [Fact]
    public async Task Publish_WhenTheBrokerEchoesTheOwnEnvelope_StillDeliversExactlyOnce()
    {
        await using Bus bus = await Bus.StartAsync(broker: true);

        bus.Publisher.Publish(
            new DhcpLeaseChangedEvent(
                DateTimeOffset.UtcNow,
                Guid.NewGuid(),
                DhcpLeaseChangeKindEnum.Granted
            )
        );
        await bus.Transport.WaitForPublishedAsync(1);
        await bus.Transport.EchoAsync();
        await bus.DrainAsync();

        Assert.Single(bus.Local.Leases);
        Assert.Empty(bus.Remote.Zones);
    }

    [Fact]
    public async Task Publish_OfSettingsChanged_IsDeliveredLocallyAndNeverSentToTheBroker()
    {
        await using Bus bus = await Bus.StartAsync(broker: true);

        bus.Publisher.Publish(
            new ClusterConfigSettingsChangedEvent(DateTimeOffset.UtcNow, "EventBroker", "[]")
        );
        await bus.DrainAsync();

        Assert.Single(bus.Local.Settings);
        Assert.Empty(bus.Transport.PublishedExceptMarker);
    }

    [Fact]
    public async Task ReceiveFromBroker_OfAnotherInstancesEvent_ReachesOnlyRemoteAwareSubscribers()
    {
        await using Bus bus = await Bus.StartAsync(broker: true);

        DnsServerEventEnvelope envelope = DnsServerEventEnvelopeCodec.Encode(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.test"),
            Guid.NewGuid()
        );
        await bus.Transport.PushAsync(envelope);
        await bus.DrainAsync();

        Assert.Single(bus.Remote.Zones);
        Assert.Empty(bus.Local.Zones);
    }

    [Fact]
    public async Task ReceiveFromBroker_OfAnEnvelopeWithNoOrigin_CountsAsRemote()
    {
        await using Bus bus = await Bus.StartAsync(broker: true);

        DnsServerEventEnvelope envelope = DnsServerEventEnvelopeCodec.Encode(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.test"),
            Guid.Empty
        );
        await bus.Transport.PushAsync(envelope);
        await bus.DrainAsync();

        Assert.Single(bus.Remote.Zones);
        Assert.Empty(bus.Local.Zones);
    }

    [Fact]
    public async Task ReceiveFromBroker_OfSettingsOrUnknownNames_CallsNobody()
    {
        await using Bus bus = await Bus.StartAsync(broker: true);

        await bus.Transport.PushAsync(
            new DnsServerEventEnvelope(
                "ClusterConfigSettingsChanged",
                """{"OccurredAtUtc":"2026-01-01T00:00:00+00:00","SectionName":"x","SettingsJson":"[]"}""",
                Guid.NewGuid()
            )
        );
        await bus.Transport.PushAsync(
            new DnsServerEventEnvelope("FromTheFuture", "{}", Guid.NewGuid())
        );
        await bus.DrainAsync();

        Assert.Empty(bus.Remote.Settings);
        Assert.Empty(bus.Local.Settings);
        Assert.Empty(bus.Remote.Zones);
    }

    /// <summary>
    ///     The rewrite settings section is cluster-synced over the cluster channel only: its
    ///     settings event stays in this process and never reaches the broker.
    /// </summary>
    [Fact]
    public async Task Publish_OfTheRewriteSettingsChange_IsDeliveredLocallyAndNeverSentToTheBroker()
    {
        await using Bus bus = await Bus.StartAsync(broker: true);

        bus.Publisher.Publish(
            new ClusterConfigSettingsChangedEvent(
                DateTimeOffset.UtcNow,
                "DnsServer:Rewrites",
                """[{"SectionPath":"DnsServer:Rewrites","Key":"Enable","Value":true}]"""
            )
        );
        await bus.DrainAsync();

        Assert.Single(bus.Local.Settings);
        Assert.Empty(bus.Transport.PublishedExceptMarker);
    }

    /// <summary>
    ///     A rewrite settings envelope from another instance must not drive a settings write: the
    ///     codec refuses the type, so no subscriber sees it.
    /// </summary>
    [Fact]
    public async Task ReceiveFromBroker_OfAnotherInstancesRewriteSettingsChange_CallsNobody()
    {
        await using Bus bus = await Bus.StartAsync(broker: true);

        await bus.Transport.PushAsync(
            new DnsServerEventEnvelope(
                "ClusterConfigSettingsChanged",
                """{"OccurredAtUtc":"2026-01-01T00:00:00+00:00","SectionName":"DnsServer:Rewrites","SettingsJson":"[{\"SectionPath\":\"DnsServer:Rewrites\",\"Key\":\"Enable\",\"Value\":true}]"}""",
                Guid.NewGuid()
            )
        );
        await bus.DrainAsync();

        Assert.Empty(bus.Remote.Settings);
        Assert.Empty(bus.Local.Settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_WhenASubscriberThrows_TheNextSubscriberStillRuns(bool asyncFailure)
    {
        await using Bus bus = await Bus.StartAsync(
            broker: false,
            first: new ThrowingZoneSubscriber(asyncFailure)
        );

        bus.Publisher.Publish(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "a.test")
        );
        await bus.DrainAsync();

        Assert.Single(bus.Local.Zones);
        Assert.Contains(
            bus.DispatcherLogger.Errors,
            message =>
                message.Contains(nameof(ThrowingZoneSubscriber))
                && message.Contains(nameof(DnsZoneChangedEvent))
        );
    }

    /// <summary>
    ///     The log carries the exception the subscriber threw, not a wrapper around it, whether
    ///     the subscriber throws before returning a task or from the task it returns.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_WhenASubscriberThrows_TheLoggedExceptionIsTheOriginalOne(
        bool asyncFailure
    )
    {
        await using Bus bus = await Bus.StartAsync(
            broker: false,
            first: new ThrowingZoneSubscriber(asyncFailure)
        );

        bus.Publisher.Publish(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "a.test")
        );
        await bus.DrainAsync();

        Exception logged = Assert.Single(bus.DispatcherLogger.Exceptions);
        Assert.IsType<InvalidOperationException>(logged);
        Assert.Equal(asyncFailure ? "asynchronous failure" : "synchronous failure", logged.Message);
    }

    /// <summary>
    ///     Events of three types, published interleaved, each reach the subscribers of their own
    ///     type only and in publish order within the type.
    /// </summary>
    [Fact]
    public async Task Publish_OfThousandsOfEventsOfThreeTypes_ReachesTheRightSubscribersInOrder()
    {
        const int PerType = 1_000;
        await using Bus bus = await Bus.StartAsync(broker: false, capacity: 5_000, start: false);
        Guid[] leaseIds = [.. Enumerable.Range(0, PerType).Select(_ => Guid.NewGuid())];

        for (int index = 0; index < PerType; index++)
        {
            bus.Publisher.Publish(
                new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), $"z{index}.test")
            );
            bus.Publisher.Publish(
                new DhcpLeaseChangedEvent(
                    DateTimeOffset.UtcNow,
                    leaseIds[index],
                    DhcpLeaseChangeKindEnum.Granted
                )
            );
            bus.Publisher.Publish(
                new DnsQueryAnsweredEvent(
                    DateTimeOffset.UtcNow,
                    DnsTransportProtocolEnum.Udp,
                    IPAddress.Loopback,
                    $"q{index}.test",
                    DnsResourceRecordTypeEnum.A,
                    DnsResponseCodeEnum.NoError,
                    0
                )
            );
        }

        await bus.StartDispatcherAsync();
        await bus.DrainAsync();

        Assert.Equal(
            Enumerable.Range(0, PerType).Select(index => $"z{index}.test"),
            bus.Local.Zones.Select(item => item.ZoneName)
        );
        Assert.Equal(leaseIds, bus.Local.Leases.Select(item => item.LeaseId));
        Assert.Equal(
            Enumerable.Range(0, PerType).Select(index => $"q{index}.test"),
            bus.Local.Queries.Select(item => item.QueriedName)
        );
        Assert.Empty(bus.Local.Settings);
    }

    [Fact]
    public async Task Publish_WhenTheQueryQueueOverflows_KeepsControlEventsAndWarnsOnceWithTheCount()
    {
        await using Bus bus = await Bus.StartAsync(broker: false, capacity: 2, start: false);

        bus.Publisher.Publish(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "a.test")
        );

        for (int index = 0; index < 5; index++)
            bus.Publisher.Publish(
                new DnsQueryAnsweredEvent(
                    DateTimeOffset.UtcNow,
                    DnsTransportProtocolEnum.Udp,
                    IPAddress.Loopback,
                    $"q{index}.test",
                    DnsResourceRecordTypeEnum.A,
                    DnsResponseCodeEnum.NoError,
                    0
                )
            );

        await bus.StartDispatcherAsync();
        await Bus.UntilAsync(() =>
            bus.Local.Zones.Count == 1
            && bus.Local.Queries.Count == 2
            && bus.PublisherLogger.Entries.Any(entry => entry.Level == LogLevel.Warning)
        );

        Assert.Single(bus.Local.Zones);
        Assert.Equal(2, bus.Local.Queries.Count);
        string warning = Assert
            .Single(
                bus.PublisherLogger.Entries.Where(entry => entry.Level == LogLevel.Warning),
                entry => entry.Message.Contains("dropped")
            )
            .Message;
        Assert.Contains("3 query events", warning);
    }

    private sealed class LocalSubscriber
        : IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            IDnsServerEventSubscriber<DhcpLeaseChangedEvent>,
            IDnsServerEventSubscriber<ClusterConfigSettingsChangedEvent>,
            IDnsServerEventSubscriber<DnsQueryAnsweredEvent>
    {
        public const string MarkerName = "drain-marker.test";
        private TaskCompletionSource _marker = new();

        public List<DnsZoneChangedEvent> Zones { get; } = [];
        public List<DhcpLeaseChangedEvent> Leases { get; } = [];
        public List<ClusterConfigSettingsChangedEvent> Settings { get; } = [];
        public List<DnsQueryAnsweredEvent> Queries { get; } = [];

        public ValueTask HandleAsync(DnsZoneChangedEvent e, CancellationToken t) => Add(Zones, e);

        public ValueTask HandleAsync(DhcpLeaseChangedEvent e, CancellationToken t) =>
            Add(Leases, e);

        public ValueTask HandleAsync(ClusterConfigSettingsChangedEvent e, CancellationToken t) =>
            Add(Settings, e);

        public ValueTask HandleAsync(DnsQueryAnsweredEvent e, CancellationToken t)
        {
            if (e.QueriedName == MarkerName)
            {
                _marker.TrySetResult();
                return ValueTask.CompletedTask;
            }

            return Add(Queries, e);
        }

        public void ArmMarker() => _marker = new TaskCompletionSource();

        public Task MarkerSeen => _marker.Task;

        private static ValueTask Add<T>(List<T> list, T item)
        {
            lock (list)
                list.Add(item);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RemoteSubscriber
        : IDnsServerRemoteEventSubscriber,
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            IDnsServerEventSubscriber<ClusterConfigSettingsChangedEvent>
    {
        public List<DnsZoneChangedEvent> Zones { get; } = [];
        public List<ClusterConfigSettingsChangedEvent> Settings { get; } = [];

        public ValueTask HandleAsync(DnsZoneChangedEvent e, CancellationToken t)
        {
            lock (Zones)
                Zones.Add(e);
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(ClusterConfigSettingsChangedEvent e, CancellationToken t)
        {
            lock (Settings)
                Settings.Add(e);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingZoneSubscriber(bool asyncFailure)
        : IDnsServerEventSubscriber<DnsZoneChangedEvent>
    {
        public async ValueTask HandleAsync(DnsZoneChangedEvent e, CancellationToken t)
        {
            if (!asyncFailure)
                throw new InvalidOperationException("synchronous failure");

            await Task.Yield();
            throw new InvalidOperationException("asynchronous failure");
        }
    }

    private sealed class FakeTransport : IDnsServerEventBrokerTransport
    {
        private readonly TaskCompletionSource<
            Func<DnsServerEventEnvelope, CancellationToken, Task>
        > _callback = new();

        public List<DnsServerEventEnvelope> Published { get; } = [];
        public bool IsConnected => true;

        public DnsServerEventEnvelope[] PublishedExceptMarker
        {
            get
            {
                lock (Published)
                    return [.. Published.Where(item => item.EventTypeName != "DnsQueryAnswered")];
            }
        }

        public Task PublishAsync(DnsServerEventEnvelope envelope, CancellationToken stoppingToken)
        {
            lock (Published)
                Published.Add(envelope);
            return Task.CompletedTask;
        }

        public async Task StartConsumingAsync(
            Func<DnsServerEventEnvelope, CancellationToken, Task> onEnvelopeReceived,
            CancellationToken stoppingToken
        )
        {
            _callback.TrySetResult(onEnvelopeReceived);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) { }
        }

        public async Task PushAsync(DnsServerEventEnvelope envelope) =>
            await (await _callback.Task.WaitAsync(Wait))(envelope, CancellationToken.None);

        public async Task EchoAsync()
        {
            DnsServerEventEnvelope[] copy;
            lock (Published)
                copy = [.. Published];

            foreach (DnsServerEventEnvelope envelope in copy)
                await PushAsync(envelope);
        }

        /// <summary>
        ///     This method waits until the transport holds at least <paramref name="count" />
        ///     drain-marker envelopes, so a test knows the broker pump has published everything it
        ///     queued before the markers.
        /// </summary>
        public async Task WaitForMarkersAsync(int count)
        {
            DateTime deadline = DateTime.UtcNow + Wait;

            while (DateTime.UtcNow < deadline)
            {
                lock (Published)
                    if (Published.Count(item => item.EventTypeName == "DnsQueryAnswered") >= count)
                        return;

                await Task.Delay(10);
            }

            throw new TimeoutException("The broker transport did not receive the drain marker.");
        }

        public async Task WaitForPublishedAsync(int count)
        {
            DateTime deadline = DateTime.UtcNow + Wait;

            while (DateTime.UtcNow < deadline)
            {
                lock (Published)
                    if (Published.Count >= count)
                        return;

                await Task.Delay(10);
            }

            throw new TimeoutException("The broker transport did not receive the envelope.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Bus : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly List<IHostedService> _services = [];
        private readonly bool _broker;
        private int _markersPublished;

        private Bus(ServiceProvider provider, bool broker)
        {
            _provider = provider;
            _broker = broker;
        }

        public DnsServerEventPublisher Publisher =>
            _provider.GetRequiredService<DnsServerEventPublisher>();
        public LocalSubscriber Local => _provider.GetRequiredService<LocalSubscriber>();
        public RemoteSubscriber Remote => _provider.GetRequiredService<RemoteSubscriber>();
        public FakeTransport Transport => _provider.GetRequiredService<FakeTransport>();
        public CapturingLogger<DnsServerEventDispatcher> DispatcherLogger =>
            _provider.GetRequiredService<CapturingLogger<DnsServerEventDispatcher>>();
        public CapturingLogger<DnsServerEventPublisher> PublisherLogger =>
            _provider.GetRequiredService<CapturingLogger<DnsServerEventPublisher>>();

        public static async Task<Bus> StartAsync(
            bool broker,
            int capacity = 100,
            bool start = true,
            object first = null
        )
        {
            ServiceCollection services = new();
            services.AddSingleton(
                Options.Create(
                    new DnsServerEventPublisherOptions
                    {
                        Capacity = capacity,
                        Transport = broker
                            ? EventTransportEnum.RabbitMq
                            : EventTransportEnum.InMemory,
                    }
                )
            );
            services.AddSingleton<CapturingLogger<DnsServerEventPublisher>>();
            services.AddSingleton<ILogger<DnsServerEventPublisher>>(p =>
                p.GetRequiredService<CapturingLogger<DnsServerEventPublisher>>()
            );
            services.AddSingleton<CapturingLogger<DnsServerEventDispatcher>>();
            services.AddSingleton<ILogger<DnsServerEventDispatcher>>(p =>
                p.GetRequiredService<CapturingLogger<DnsServerEventDispatcher>>()
            );
            services.AddSingleton<ILogger<BrokerEventConsumerService>>(
                NullLogger<BrokerEventConsumerService>.Instance
            );
            services.AddSingleton<FakeTransport>();
            services.AddSingleton<IDnsServerEventBrokerTransport>(p =>
                p.GetRequiredService<FakeTransport>()
            );
            services.AddSingleton<DnsServerEventPublisher>();
            services.AddSingleton<LocalSubscriber>();
            services.AddSingleton<RemoteSubscriber>();

            // The throwing subscriber registers first so the one after it proves isolation.
            if (first is ThrowingZoneSubscriber throwing)
                services.AddSingleton<IDnsServerEventSubscriber<DnsZoneChangedEvent>>(throwing);

            services.AddSingleton<IDnsServerEventSubscriber<DnsZoneChangedEvent>>(p =>
                p.GetRequiredService<LocalSubscriber>()
            );
            services.AddSingleton<IDnsServerEventSubscriber<DhcpLeaseChangedEvent>>(p =>
                p.GetRequiredService<LocalSubscriber>()
            );
            services.AddSingleton<IDnsServerEventSubscriber<ClusterConfigSettingsChangedEvent>>(p =>
                p.GetRequiredService<LocalSubscriber>()
            );
            services.AddSingleton<IDnsServerEventSubscriber<DnsQueryAnsweredEvent>>(p =>
                p.GetRequiredService<LocalSubscriber>()
            );
            services.AddSingleton<IDnsServerEventSubscriber<DnsZoneChangedEvent>>(p =>
                p.GetRequiredService<RemoteSubscriber>()
            );
            services.AddSingleton<IDnsServerEventSubscriber<ClusterConfigSettingsChangedEvent>>(p =>
                p.GetRequiredService<RemoteSubscriber>()
            );

            services.AddSingleton<ILogger<BrokerEventPublishService>>(
                NullLogger<BrokerEventPublishService>.Instance
            );
            services.AddSingleton<BrokerEventConsumerService>();
            services.AddSingleton<BrokerEventPublishService>();
            services.AddSingleton<DnsServerEventDispatcher>();

            Bus bus = new(services.BuildServiceProvider(), broker);

            if (broker)
            {
                await bus.StartServiceAsync(
                    bus._provider.GetRequiredService<BrokerEventConsumerService>()
                );
                await bus.StartServiceAsync(
                    bus._provider.GetRequiredService<BrokerEventPublishService>()
                );
            }

            if (start)
                await bus.StartDispatcherAsync();

            return bus;
        }

        public Task StartDispatcherAsync() =>
            StartServiceAsync(_provider.GetRequiredService<DnsServerEventDispatcher>());

        private async Task StartServiceAsync(IHostedService service)
        {
            await service.StartAsync(CancellationToken.None);
            _services.Add(service);
        }

        /// <summary>
        ///     This method waits until everything published before it has been dispatched. The
        ///     marker is a per-query event: the dispatcher reads every control event first, then
        ///     the per-query queue in order, so the marker is the last event handled locally. With a
        ///     broker, broker publishing is a separate pump (one FIFO reader), so the local marker
        ///     says nothing about it: the method also waits until the transport holds the marker's
        ///     envelope. The marker crosses the broker, so every earlier broker-bound event has
        ///     been published by then.
        /// </summary>
        public async Task DrainAsync()
        {
            Local.ArmMarker();

            Publisher.Publish(
                new DnsQueryAnsweredEvent(
                    DateTimeOffset.UtcNow,
                    DnsTransportProtocolEnum.Udp,
                    IPAddress.Loopback,
                    LocalSubscriber.MarkerName,
                    DnsResourceRecordTypeEnum.A,
                    DnsResponseCodeEnum.NoError,
                    0
                )
            );

            await Local.MarkerSeen.WaitAsync(Wait);

            if (_broker)
                await Transport.WaitForMarkersAsync(++_markersPublished);
        }

        /// <summary>
        ///     This method polls until a condition holds, for tests that cannot use a marker.
        /// </summary>
        public static async Task UntilAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + Wait;

            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("The condition did not become true.");

                await Task.Delay(10);
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (IHostedService service in _services)
                await service.StopAsync(CancellationToken.None);

            await _provider.DisposeAsync();
        }
    }
}
