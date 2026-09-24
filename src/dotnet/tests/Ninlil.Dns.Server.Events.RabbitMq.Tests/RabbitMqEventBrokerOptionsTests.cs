// Define our namespace.
namespace Ninlil.Dns.Server.Events.RabbitMq.Tests;

public class RabbitMqEventBrokerOptionsTests
{
    [Fact]
    public void DefaultsAreASensibleLocalBrokerAndAFanoutExchangeName()
    {
        RabbitMqEventBrokerOptions options = new();

        Assert.Equal("localhost", options.HostName);
        Assert.Equal(5672, options.Port);
        Assert.Equal("guest", options.UserName);
        Assert.Equal("guest", options.Password);
        Assert.Equal("/", options.VirtualHost);
        Assert.Equal("ninlil-dns-events", options.ExchangeName);
    }

    [Fact]
    public void ReconnectBackoffCeilingIsAtOrAboveTheInitialDelay()
    {
        RabbitMqEventBrokerOptions options = new();

        Assert.True(options.MaxReconnectDelay >= options.InitialReconnectDelay);
    }
}
