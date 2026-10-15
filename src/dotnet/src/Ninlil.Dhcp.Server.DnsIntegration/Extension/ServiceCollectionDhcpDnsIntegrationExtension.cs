using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ninlil.Dhcp.Server.Core.DnsUpdate;
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
    ///     This method registers the DHCP-to-DNS feature. It adds, all reacting to
    ///     <see cref="DhcpLeaseChangedEvent" /> unless noted:
    ///     <list type="bullet">
    ///         <item>
    ///             the scoped <see cref="DhcpDnsRegistrationService" /> and its subscriber, which
    ///             takes turns on the singleton <see cref="DhcpDnsRegistrationGate" /> so
    ///             registrations run one at a time;
    ///         </item>
    ///         <item>
    ///             the scoped <see cref="DhcpClientRegistryAutoPopulationService" /> and its
    ///             subscriber, which adds new clients to the client registry;
    ///         </item>
    ///         <item>
    ///             the singleton <see cref="DhcpLeaseClientIdentityResolver" />, under its own type
    ///             only, since <c>DhcpServiceRegistration</c> builds the
    ///             <c>CompositeClientIdentityResolver</c> itself, and the
    ///             <see cref="DhcpLeaseIdentitySnapshotInvalidator" /> subscriber that refreshes its
    ///             lease snapshot on every lease change;
    ///         </item>
    ///         <item>
    ///             the scoped <see cref="DhcpDnsUpdatePolicy" />, which replaces the engine's
    ///             default <see cref="IDhcpDnsUpdatePolicy" /> so a DHCPv6 Client FQDN reply says
    ///             S=1 for a scope with an eligible DNS zone.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         The caller must already have registered the <c>Ninlil.Dns.Server.Core</c> event bus,
    ///         the DHCP and DNS repositories and <c>IZoneUpdateService</c>, as with other features
    ///         that write DNS records (for example the CAA registration in
    ///         <c>Ninlil.Dns.Server.Acme.Extension.ServiceCollectionAcmeExtension</c>).
    ///         <c>Program.cs</c> calls this method in every configuration, whichever engine or relay
    ///         is active: lease rows come only from a local engine and the expiry sweep runs
    ///         everywhere, so with a relay the integration sees no grants and still removes the
    ///         records of leases left from earlier local operation when they expire. The order
    ///         relative to the engine registrations does not matter. It opens no socket.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpDnsIntegration(this IServiceCollection instance)
    {
        // Scoped for the same reason as IZoneUpdateService itself: it depends on the scoped
        // repositories and unit of work the persistence layer registers.
        instance.AddScoped<DhcpDnsRegistrationService>();

        // Singleton: one gate for the process, so every subscriber instance waits on the same one.
        instance.AddSingleton<DhcpDnsRegistrationGate>();

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

        // Replace, not Add: the engine registers a default policy with TryAdd, and this one must
        // win in either order of the two calls.
        instance.Replace(ServiceDescriptor.Scoped<IDhcpDnsUpdatePolicy, DhcpDnsUpdatePolicy>());

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
