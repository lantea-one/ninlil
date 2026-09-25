using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Dns.Server.Events.RabbitMq.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's RabbitMQ event-transport implementation.
/// </summary>
public static class ServiceCollectionRabbitMqEventsExtension
{
    /// <summary>
    ///     This method registers <see cref="IDnsServerEventBrokerTransport" />, backed by
    ///     <see cref="RabbitMqDnsServerEventBrokerTransport" />. <c>Ninlil.Web</c> calls it only
    ///     when <c>DnsServer:Events:Transport</c> is
    ///     <see cref="Common.Enum.EventTransportEnum.RabbitMq" />. Without it, the optional
    ///     <c>brokerTransport</c> parameter of
    ///     <see cref="Server.Core.Events.DnsServerEventPublisher" /> stays unresolved and events
    ///     use the in-memory transport.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read RabbitMQ settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerEventsRabbitMq(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<RabbitMqEventBrokerOptions>(
            configuration.GetSection(RabbitMqEventBrokerOptions.SectionName)
        );

        instance.AddSingleton<
            IDnsServerEventBrokerTransport,
            RabbitMqDnsServerEventBrokerTransport
        >();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
