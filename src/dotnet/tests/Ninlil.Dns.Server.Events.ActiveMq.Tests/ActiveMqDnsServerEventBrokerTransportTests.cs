using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Events.ActiveMq.Tests;

public class ActiveMqDnsServerEventBrokerTransportTests
{
    [Fact]
    public async Task PublishAsyncFailsFastRatherThanBlockingWhenNeverYetConnected()
    {
        // 203.0.113.1 is RFC 5737 TEST-NET-3, guaranteed unreachable -- this asserts the real
        // bug this project's own real-container smoke test found and fixed: connection.Start()
        // blocks indefinitely (confirmed against a real broker container) until a connection
        // ever succeeds, so PublishAsync must never itself wait on that; it should throw
        // immediately by checking whether the background connect attempt has already
        // succeeded, rather than ever blocking the caller.
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
                new DnsCacheEvictedEvent(DateTimeOffset.UtcNow, "test", 1),
                CancellationToken.None
            )
        );

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
    }
}
