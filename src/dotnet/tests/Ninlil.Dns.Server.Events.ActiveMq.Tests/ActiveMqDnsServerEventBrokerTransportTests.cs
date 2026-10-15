using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Dns.Server.Events.ActiveMq.Tests;

/// <summary>
///     This class tests <see cref="ActiveMqDnsServerEventBrokerTransport" /> without a broker,
///     pointing it at an unreachable address to check that publishing never waits for the
///     first connection.
/// </summary>
public class ActiveMqDnsServerEventBrokerTransportTests
{
    [Fact]
    public async Task PublishAsyncFailsFastRatherThanBlockingWhenNeverYetConnected()
    {
        // 203.0.113.1 is in RFC 5737 TEST-NET-3, so nothing answers. connection.Start() blocks
        // until the broker has been reachable once; PublishAsync must throw at once instead of
        // waiting on the background connect.
        ActiveMqEventBrokerOptions options = new()
        {
            HostName = "203.0.113.1",
            Port = 61616,
            InitialReconnectDelay = TimeSpan.FromMilliseconds(100),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(100),
        };

        await using ActiveMqDnsServerEventBrokerTransport transport = new(
            Options.Create(options),
            NullLogger<ActiveMqDnsServerEventBrokerTransport>.Instance
        );

        DateTimeOffset started = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.PublishAsync(
                DnsServerEventEnvelopeCodec.Encode(
                    new DnsCacheEvictedEvent(DateTimeOffset.UtcNow, "test", 1),
                    Guid.NewGuid()
                ),
                CancellationToken.None
            )
        );

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
    }
}
