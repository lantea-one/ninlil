// Define our namespace.
namespace Ninlil.Dns.Server.Events.ActiveMq.Tests;

public class ActiveMqEventBrokerOptionsTests
{
    [Fact]
    public void DefaultsAreASensibleLocalBrokerAndASharedTopicName()
    {
        ActiveMqEventBrokerOptions options = new();

        Assert.Equal("localhost", options.HostName);
        Assert.Equal(61616, options.Port);
        Assert.Equal("admin", options.UserName);
        Assert.Equal("admin", options.Password);
        Assert.Equal("ninlil-dns-events", options.TopicName);
    }

    [Fact]
    public void ReconnectBackoffCeilingIsAtOrAboveTheInitialDelay()
    {
        ActiveMqEventBrokerOptions options = new();

        Assert.True(options.MaxReconnectDelay >= options.InitialReconnectDelay);
    }

    [Fact]
    public void PublishTimeoutIsAPositiveBoundedDuration()
    {
        ActiveMqEventBrokerOptions options = new();

        Assert.True(options.PublishTimeout > TimeSpan.Zero);
    }
}
