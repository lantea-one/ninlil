using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Server.Core.Extension;
using Ninlil.Dhcp.Server.DnsIntegration;
using Ninlil.Dhcp.Server.DnsIntegration.Extension;
using Ninlil.Dhcp.Server.Relay;
using Ninlil.Dhcp.Server.Relay.Extension;
using Ninlil.Dns.Server.ClientIdentity;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class holds the DHCP service registration that <c>Program.cs</c> calls: the DHCPv4 and
///     DHCPv6 engine choice (local engine or relay agent), the DHCP-to-DNS integration and the
///     client identity resolver chain. It is separate from <c>Program.cs</c> so a test can build
///     the registrations on a plain <see cref="IServiceCollection" />.
/// </summary>
public static class DhcpServiceRegistration
{
    /// <summary>
    ///     This method registers the DHCPv4 and DHCPv6 request handlers, each either the local engine
    ///     or its relay agent, as chosen by <see cref="Dhcpv4RelayOptions.Enable" /> and
    ///     <see cref="Dhcpv6RelayOptions.Enable" />. The DHCP repositories must already be registered.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The application configuration the relay flags and options come from.</param>
    /// <returns>The <paramref name="services" /> instance.</returns>
    public static IServiceCollection AddNinlilDhcpEngines(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // The DHCPv4 UDP transport is registered always, like the optional DNS transports above.
        // Dhcpv4ListenerHostedService checks Dhcpv4ProtocolOptions.Enable once at boot; the listener
        // (Dhcpv4UdpListener) can still be started and stopped later whatever the flag says (see
        // IDhcpProtocolService). The flag is off by default because a second DHCP server on a network
        // that already has one causes real disruption.
        //
        // The request handler is an either/or choice: the server either answers from its own scopes and
        // leases (Ninlil.Dhcp.Server.Core) or forwards to an upstream DHCPv4 server as an RFC 3046 relay
        // agent (Ninlil.Dhcp.Server.Relay), never both. Dhcpv4RelayOptions.Enable is read here, before
        // dependency injection is built, as DnsServerEventPublisherOptions.Transport is above, rather than
        // registering both and letting the last registration win. Either branch must come after the DHCP
        // repository registration above, since AddNinlilDhcpServerCore depends on those repositories
        // (AddNinlilDhcpv4Relay depends on none).
        bool dhcpv4RelayEnabled =
            configuration
                .GetSection(Dhcpv4RelayOptions.SectionName)
                .Get<Dhcpv4RelayOptions>()
                ?.Enable
            ?? false;

        if (dhcpv4RelayEnabled)
        {
            services.AddNinlilDhcpv4Relay(configuration);
        }
        else
        {
            services.AddNinlilDhcpServerCore(configuration);

            // DHCP-to-DNS integration (registering a lease's host name as an A/AAAA record in the scope's
            // DnsZoneId) needs the local lease engine; a relay grants no leases and publishes no
            // DhcpLeaseChangedEvent, so it is registered only in this branch.
            services.AddNinlilDhcpDnsIntegration();
        }

        // The DHCPv6 UDP transport is registered always, independently of the DHCPv4 choice above.
        // Dhcpv6ListenerHostedService checks Dhcpv6ProtocolOptions.Enable once at boot, off by default
        // for the same reason as DHCPv4. The DHCPv6 handler is its own either/or choice: the local engine
        // (Ninlil.Dhcp.Server.Core) or a single-hop RFC 8415 relay agent (Ninlil.Dhcp.Server.Relay),
        // decided from Dhcpv6RelayOptions.Enable before dependency injection is built. The DHCPv4 and
        // DHCPv6 choices are independent of each other.
        bool dhcpv6RelayEnabled =
            configuration
                .GetSection(Dhcpv6RelayOptions.SectionName)
                .Get<Dhcpv6RelayOptions>()
                ?.Enable
            ?? false;

        if (dhcpv6RelayEnabled)
        {
            services.AddNinlilDhcpv6Relay(configuration);
        }
        else
        {
            services.AddNinlilDhcpv6ServerCore(configuration);
        }

        // We're done, return the IServiceCollection instance.
        return services;
    }

    /// <summary>
    ///     This method registers the <see cref="IClientIdentityResolver" /> chain: the static address
    ///     mapping resolver, then the DHCP-lease resolver (only with the local DHCPv4 engine), then
    ///     the ARP resolver.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The application configuration the relay flag comes from.</param>
    /// <returns>The <paramref name="services" /> instance.</returns>
    public static IServiceCollection AddNinlilClientIdentityResolverChain(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        bool dhcpv4RelayEnabled =
            configuration
                .GetSection(Dhcpv4RelayOptions.SectionName)
                .Get<Dhcpv4RelayOptions>()
                ?.Enable
            ?? false;

        // The static-address-mapping resolver is always tried first, so an operator's explicit mapping
        // wins over ARP or DHCP data (see StaticAddressMappingClientIdentityResolver).
        //
        // The DHCP-lease resolver only makes sense with the local DHCPv4 engine (dhcpv4RelayEnabled,
        // read above), since a relay grants no leases to look up; this is the same condition as
        // AddNinlilDhcpDnsIntegration. When present it comes second, as the more authoritative source
        // when this server issued the lease. The ARP resolver is the last fallback, since only it covers
        // a client whose lease came from another DHCP server. The choice is made here, before dependency
        // injection is built, like the broker, OIDC and DHCP engine choices.
        if (!dhcpv4RelayEnabled)
        {
            services.AddSingleton<IClientIdentityResolver>(
                provider => new CompositeClientIdentityResolver([
                    provider.GetRequiredService<StaticAddressMappingClientIdentityResolver>(),
                    provider.GetRequiredService<DhcpLeaseClientIdentityResolver>(),
                    provider.GetRequiredService<ArpNeighborTableClientIdentityResolver>(),
                ])
            );
        }
        else
        {
            services.AddSingleton<IClientIdentityResolver>(
                provider => new CompositeClientIdentityResolver([
                    provider.GetRequiredService<StaticAddressMappingClientIdentityResolver>(),
                    provider.GetRequiredService<ArpNeighborTableClientIdentityResolver>(),
                ])
            );
        }

        // We're done, return the IServiceCollection instance.
        return services;
    }
}
