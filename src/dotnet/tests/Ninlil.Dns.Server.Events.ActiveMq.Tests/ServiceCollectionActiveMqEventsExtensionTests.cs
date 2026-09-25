using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Dns.Server.Events.ActiveMq.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Events.ActiveMq.Tests;

/// <summary>
///     This class tests <see cref="ServiceCollectionActiveMqEventsExtension" /> by building a
///     service provider and resolving the transport and its options; no broker is contacted.
/// </summary>
public class ServiceCollectionActiveMqEventsExtensionTests
{
    [Fact]
    public async Task RegistersTheActiveMqTransportAsTheBrokerTransportInterface()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.AddNinlilDnsServerEventsActiveMq(configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();

        IDnsServerEventBrokerTransport transport =
            provider.GetRequiredService<IDnsServerEventBrokerTransport>();

        Assert.IsType<ActiveMqDnsServerEventBrokerTransport>(transport);
    }

    [Fact]
    public async Task BindsOptionsFromTheExpectedConfigurationSection()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["DnsServer:Events:ActiveMq:HostName"] = "broker.example.internal",
                    ["DnsServer:Events:ActiveMq:TopicName"] = "custom-topic",
                }
            )
            .Build();

        services.AddNinlilDnsServerEventsActiveMq(configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();

        ActiveMqEventBrokerOptions options = provider
            .GetRequiredService<IOptions<ActiveMqEventBrokerOptions>>()
            .Value;

        Assert.Equal("broker.example.internal", options.HostName);
        Assert.Equal("custom-topic", options.TopicName);
    }
}
