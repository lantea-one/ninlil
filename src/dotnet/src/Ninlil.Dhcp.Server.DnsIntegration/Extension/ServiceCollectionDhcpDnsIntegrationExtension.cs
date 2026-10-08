using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DHCP-&gt;DNS integration feature.
/// </summary>
public static class ServiceCollectionDhcpDnsIntegrationExtension
{
    /// <summary>
    ///     This method registers the DHCP-to-DNS feature: the scoped
    ///     <see cref="DhcpDnsRegistrationService" /> and its <see cref="DhcpLeaseChangedEvent" />
    ///     subscriber, the scoped <see cref="DhcpClientRegistryAutoPopulationService" /> and its
    ///     subscriber (a second reaction to the same event that adds new clients to the client
    ///     registry), and the singleton <see cref="DhcpLeaseClientIdentityResolver" />, under its
    ///     own type only, since <c>Program.cs</c> builds the
    ///     <c>CompositeClientIdentityResolver</c> itself, with the
    ///     <see cref="DhcpLeaseIdentitySnapshotInvalidator" /> subscriber that refreshes its
    ///     lease snapshot on every lease change.
    ///     <para>
    ///         The caller must already have registered the <c>Ninlil.Dns.Server.Core</c> event bus,
    ///         the DHCP and DNS repositories and <c>IZoneUpdateService</c>, as with other features
    ///         that write DNS records (for example the CAA registration in
    ///         <c>Ninlil.Dns.Server.Acme.Extension.ServiceCollectionAcmeExtension</c>).
    ///         <c>Program.cs</c> calls this method only when the DHCPv4 engine is local, not the
    ///         relay agent. It opens no socket.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpDnsIntegration(this IServiceCollection instance)
    {
        // Scoped for the same reason as IZoneUpdateService itself: it depends on the scoped
        // repositories and unit of work the persistence layer registers.
        instance.AddScoped<DhcpDnsRegistrationService>();

        instance.AddScoped<
            IDnsServerEventSubscriber<DhcpLeaseChangedEvent>,
            DhcpLeaseDnsRegistrationEventSubscriber
        >();

        // Client registry auto-population, a second subscriber to the same event (see
        // DhcpClientRegistryAutoPopulationService).
        instance.AddScoped<DhcpClientRegistryAutoPopulationService>();
        instance.AddScoped<
            IDnsServerEventSubscriber<DhcpLeaseChangedEvent>,
            DhcpClientRegistryAutoPopulationEventSubscriber
        >();

        // Singleton: it keeps an in-memory snapshot of the active leases and creates a scope per
        // refresh to reach ILeaseRepository. The invalidator refreshes that snapshot on each
        // lease change; it is a singleton because the resolver is.
        instance.AddSingleton<DhcpLeaseClientIdentityResolver>();
        instance.AddSingleton<
            IDnsServerEventSubscriber<DhcpLeaseChangedEvent>,
            DhcpLeaseIdentitySnapshotInvalidator
        >();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
