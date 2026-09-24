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
    ///     <see cref="RabbitMqDnsServerEventBrokerTransport" />. A composition root only needs
    ///     to call this when it actually wants
    ///     <c>DnsServer:Events:Transport</c> set to <see cref="Common.Enum.EventTransportEnum.RabbitMq" />
    ///     available -- a composition root that never calls this method (or never references
    ///     this project at all) leaves <see cref="Server.Core.Events.DnsServerEventPublisher" />'s
    ///     own optional <c>brokerTransport</c> constructor parameter unresolved, which is exactly
    ///     equivalent to staying on the default in-memory transport.
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
