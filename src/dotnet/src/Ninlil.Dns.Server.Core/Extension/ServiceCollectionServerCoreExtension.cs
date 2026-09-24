using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's shared DNS server core: the query dispatcher, the channel-based processing
///     pipeline, and the internal event bus.
/// </summary>
public static class ServiceCollectionServerCoreExtension
{
    /// <summary>
    ///     This method registers <see cref="IDnsQueryDispatcher" />, <see cref="DnsQueryProcessingPipeline" />,
    ///     and the event bus (<see cref="IDnsServerEventPublisher" /> plus its background
    ///     dispatcher). It does not, on its own, register any <see cref="IDnsQueryResolver" /> --
    ///     the composition root is expected to additionally register
    ///     <c>Ninlil.Dns.Server.Authoritative</c>'s and/or
    ///     <c>Ninlil.Dns.Server.Recursive</c>'s resolver, in whichever order they should be
    ///     tried, and any <see cref="IDnsServerEventSubscriber{TEvent}" /> it wants events
    ///     delivered to.
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

        // Unlike IDnsFilterEngine/IDnsRateLimiter, DnsCookieService lives in Server.Core itself
        // (it needs no storage of its own) and is therefore always registered here, gated at
        // runtime by DnsCookieOptions.Enable rather than by whether a separate project is
        // referenced at all.
        instance.AddSingleton<DnsCookieService>();

        // IOutboundUdpSocketPool/EdnsClientSubnetService are two more cross-cutting engines that
        // live in Server.Core itself for the same reason DnsCookieService does: both
        // Server.Recursive and Server.Forwarding already depend on this project, and neither
        // needs storage of its own, so there is no reason to split either into its own project
        // the way IDnsFilterEngine/IDnsRateLimiter's *implementations* are. Each is gated at
        // runtime by its own options class instead (EnableSourcePortPool, EdnsClientSubnetOptions.Enable).
        instance.AddSingleton<IOutboundUdpSocketPool, OutboundUdpSocketPool>();
        instance.AddSingleton<EdnsClientSubnetService>();

        // Dns64SynthesisService follows the exact same shape: storage-free, lives in Server.Core
        // so both Server.Recursive and a future Server.Forwarding consumer could reach it without
        // a project reference to one another, gated at runtime by its own Dns64Options.Enable.
        instance.AddSingleton<Dns64SynthesisService>();

        // DnsServerEventPublisher is registered once, as a singleton, under both its concrete
        // type (which DnsServerEventDispatcher depends on to reach the channel it owns) and the
        // IDnsServerEventPublisher interface (which everything else should depend on instead).
        instance.AddSingleton<DnsServerEventPublisher>();
        instance.AddSingleton<IDnsServerEventPublisher>(provider =>
            provider.GetRequiredService<DnsServerEventPublisher>()
        );
        instance.AddHostedService<DnsServerEventDispatcher>();

        // A no-op when no IDnsServerEventBrokerTransport is registered (the default, in-memory
        // configuration) -- see BrokerEventConsumerService's own doc comment. Whichever broker
        // package (Ninlil.Dns.Server.Events.RabbitMq/.ActiveMq) an operator actually
        // references registers the transport itself; this project only needs the consumer loop
        // to exist so that registration has somewhere to plug into.
        instance.AddHostedService<BrokerEventConsumerService>();

        // DnsQueryProcessingPipeline is likewise registered once, as a singleton, so that a
        // transport can inject it directly to call EnqueueAsync, while also being started and
        // stopped automatically alongside the rest of the host.
        instance.AddSingleton<DnsQueryProcessingPipeline>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<DnsQueryProcessingPipeline>()
        );

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
