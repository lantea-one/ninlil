using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Dns.Server.Events.ActiveMq.Tests;

/// <summary>
///     This class tests two <see cref="ActiveMqDnsServerEventBrokerTransport" /> instances against
///     a real ActiveMQ Classic broker in a container: instance A publishes through a real
///     <see cref="DnsServerEventPublisher" /> and <see cref="BrokerEventPublishService" /> from
///     eight threads, and instance B must receive every event. It guards the shared publish
///     session (a JMS session is not thread safe), which the publish service serialises. It needs
///     Docker.
/// </summary>
[Trait("Category", "RequiresDocker")]
public sealed class ActiveMqBrokerRoundTripTests : IAsyncLifetime
{
    /// <summary>
    ///     This constant contains the pinned broker image (tag and digest, as
    ///     <c>Ninlil.Dns.Data.EngineHosts</c> pins its images).
    /// </summary>
    private const string Image =
        "apache/activemq-classic:6.1.7@sha256:b2727c008ccc51fd45a8011fa1b2f1666bcc031766f0a9e6f3598bc7fdf3cb5e";

    private const int OpenWirePort = 61616;
    private const int ProbeMarker = -1;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(OpenWirePort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(OpenWirePort))
        .Build();

    /// <inheritdoc />
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Publish_FromEightThreads_ReachesTheOtherInstanceOnce()
    {
        const int threads = 8;
        const int perThread = 25;

        ActiveMqEventBrokerOptions brokerOptions = new()
        {
            HostName = _container.Hostname,
            Port = _container.GetMappedPublicPort(OpenWirePort),
            TopicName = $"ninlil.events.test.{Guid.NewGuid():N}",
        };

        await using ActiveMqDnsServerEventBrokerTransport transportA = NewTransport(brokerOptions);
        await using ActiveMqDnsServerEventBrokerTransport transportB = NewTransport(brokerOptions);

        List<int> received = [];
        int probes = 0;
        using CancellationTokenSource stop = new(TimeSpan.FromMinutes(2));

        Task consumer = transportB.StartConsumingAsync(
            (envelope, _) =>
            {
                if (
                    DnsServerEventEnvelopeCodec.TryDecode(envelope, out IDnsServerEvent decoded)
                    && decoded is DnsCacheEvictedEvent evicted
                )
                {
                    if (evicted.EntryCount == ProbeMarker)
                        Interlocked.Increment(ref probes);
                    else
                        lock (received)
                            received.Add(evicted.EntryCount);
                }

                return Task.CompletedTask;
            },
            stop.Token
        );

        DnsServerEventPublisherOptions publisherOptions = new()
        {
            Capacity = 1000,
            Transport = EventTransportEnum.ActiveMq,
        };
        IOptions<DnsServerEventPublisherOptions> options = Options.Create(publisherOptions);
        DnsServerEventPublisher publisher = new(
            options,
            NullLogger<DnsServerEventPublisher>.Instance,
            transportA
        );
        using BrokerEventPublishService pump = new(
            publisher,
            options,
            NullLogger<BrokerEventPublishService>.Instance,
            transportA
        );
        await pump.StartAsync(CancellationToken.None);

        try
        {
            // A topic subscription only sees later messages, so probe until B is listening.
            DateTime probeDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);

            while (Volatile.Read(ref probes) == 0)
            {
                Assert.True(DateTime.UtcNow < probeDeadline, "B never received a probe.");
                publisher.Publish(
                    new DnsCacheEvictedEvent(DateTimeOffset.UtcNow, "probe", ProbeMarker)
                );
                await Task.Delay(250);
            }

            Task[] publishers = Enumerable
                .Range(0, threads)
                .Select(thread =>
                    Task.Run(() =>
                    {
                        for (int index = 0; index < perThread; index++)
                            publisher.Publish(
                                new DnsCacheEvictedEvent(
                                    DateTimeOffset.UtcNow,
                                    "load",
                                    (thread * perThread) + index
                                )
                            );
                    })
                )
                .ToArray();
            await Task.WhenAll(publishers);

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);

            while (DateTime.UtcNow < deadline)
            {
                lock (received)
                    if (received.Count >= threads * perThread)
                        break;

                await Task.Delay(50);
            }

            await Task.Delay(500);

            lock (received)
            {
                Assert.Equal(threads * perThread, received.Count);
                Assert.Equal(
                    Enumerable.Range(0, threads * perThread),
                    received.OrderBy(value => value)
                );
            }
        }
        finally
        {
            await pump.StopAsync(CancellationToken.None);
            await stop.CancelAsync();
            await consumer;
        }
    }

    private static ActiveMqDnsServerEventBrokerTransport NewTransport(
        ActiveMqEventBrokerOptions brokerOptions
    ) =>
        new(
            Options.Create(brokerOptions),
            NullLogger<ActiveMqDnsServerEventBrokerTransport>.Instance
        );
}
