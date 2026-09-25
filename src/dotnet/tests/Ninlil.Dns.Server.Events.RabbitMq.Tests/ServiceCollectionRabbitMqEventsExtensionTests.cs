using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Dns.Server.Events.RabbitMq.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Events.RabbitMq.Tests;

/// <summary>
///     This class tests <see cref="ServiceCollectionRabbitMqEventsExtension" /> by building a
///     service provider and resolving the transport and its options; no broker is contacted.
/// </summary>
public class ServiceCollectionRabbitMqEventsExtensionTests
{
    [Fact]
    public async Task RegistersTheRabbitMqTransportAsTheBrokerTransportInterface()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.AddNinlilDnsServerEventsRabbitMq(configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();

        IDnsServerEventBrokerTransport transport =
            provider.GetRequiredService<IDnsServerEventBrokerTransport>();

        Assert.IsType<RabbitMqDnsServerEventBrokerTransport>(transport);
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
                    ["DnsServer:Events:RabbitMq:HostName"] = "broker.example.internal",
                    ["DnsServer:Events:RabbitMq:ExchangeName"] = "custom-exchange",
                }
            )
            .Build();

        services.AddNinlilDnsServerEventsRabbitMq(configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();

        RabbitMqEventBrokerOptions options = provider
            .GetRequiredService<IOptions<RabbitMqEventBrokerOptions>>()
            .Value;

        Assert.Equal("broker.example.internal", options.HostName);
        Assert.Equal("custom-exchange", options.ExchangeName);
    }
}
