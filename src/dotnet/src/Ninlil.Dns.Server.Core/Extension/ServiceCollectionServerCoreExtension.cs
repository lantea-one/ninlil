using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Core.Dns64;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Extension;

/// <summary>
///     This class provides the <see cref="IServiceCollection" /> extension that registers the
///     shared DNS server core: the query pipeline and dispatcher, the event bus, the cache
///     sweeper, and the storage-free services several resolver projects share (DNS Cookies, EDNS
///     Client Subnet, DNS64 and the outbound UDP socket pool).
/// </summary>
public static class ServiceCollectionServerCoreExtension
{
    /// <summary>
    ///     This method registers <see cref="IDnsQueryDispatcher" />, <see cref="DnsQueryProcessingPipeline" />
    ///     (as a singleton and a hosted service), the event bus (<see cref="IDnsServerEventPublisher" />,
    ///     its dispatcher and the broker consumer), the cache sweeper, and the shared services
    ///     with their options. It registers no <see cref="IDnsQueryResolver" />; the resolver
    ///     projects do, in the order they should be tried, along with any
    ///     <see cref="IDnsServerEventSubscriber{TEvent}" />.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read server-core settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerCore(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DnsQueryProcessingPipelineOptions>(
            configuration.GetSection(DnsQueryProcessingPipelineOptions.SectionName)
        );
        instance.Configure<DnsServerEventPublisherOptions>(
            configuration.GetSection(DnsServerEventPublisherOptions.SectionName)
        );
        instance.Configure<DnsCookieOptions>(
            configuration.GetSection(DnsCookieOptions.SectionName)
        );
        instance.Configure<OutboundQueryOptions>(
            configuration.GetSection(OutboundQueryOptions.SectionName)
        );
        instance.Configure<EdnsClientSubnetOptions>(
            configuration.GetSection(EdnsClientSubnetOptions.SectionName)
        );
        instance.Configure<Dns64Options>(configuration.GetSection(Dns64Options.SectionName));

        instance.AddSingleton<IDnsQueryDispatcher, DnsQueryDispatcher>();

        // DnsCookieService needs no storage, so it lives here and is always registered; it is
        // switched on and off by DnsCookieOptions.Enable.
        instance.AddSingleton<DnsCookieService>();

        // Server.Recursive and Server.Forwarding both use these and both reference this project,
        // so they live here. Each is switched by its options (OutboundQueryOptions.EnableSourcePortPool,
        // EdnsClientSubnetOptions.Enable).
        instance.AddSingleton<IOutboundUdpSocketPool, OutboundUdpSocketPool>();
        instance.AddSingleton<EdnsClientSubnetService>();

        // Used by Server.Recursive; switched by Dns64Options.Enable.
        instance.AddSingleton<Dns64SynthesisService>();

        // One publisher instance under two types: DnsServerEventDispatcher needs the concrete
        // type to read its channel; everything else uses the interface.
        instance.AddSingleton<DnsServerEventPublisher>();
        instance.AddSingleton<IDnsServerEventPublisher>(provider =>
            provider.GetRequiredService<DnsServerEventPublisher>()
        );
        instance.AddHostedService<DnsServerEventDispatcher>();

        // Does nothing unless a broker package (Events.RabbitMq or Events.ActiveMq) registers
        // an IDnsServerEventBrokerTransport.
        instance.AddHostedService<BrokerEventConsumerService>();

        // Sweeps every IDnsCache the resolver projects register.
        instance.AddHostedService<DnsCacheEvictionService>();

        // One pipeline instance: transports inject it to call EnqueueAsync, and the host starts
        // and stops it.
        instance.AddSingleton<DnsQueryProcessingPipeline>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<DnsQueryProcessingPipeline>()
        );

        return instance;
    }
}
