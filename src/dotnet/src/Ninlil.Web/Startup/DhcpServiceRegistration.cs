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
    ///     <see cref="Dhcpv6RelayOptions.Enable" />, and the DHCP-to-DNS integration in every
    ///     configuration. The DHCP repositories must already be registered.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The application configuration the relay flags and options come from.</param>
    /// <returns>The <paramref name="services" /> instance.</returns>
    public static IServiceCollection AddNinlilDhcpEngines(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // The DHCPv4 UDP transport is registered always, like the optional DNS transports.
        // Dhcpv4ListenerHostedService checks Dhcpv4ProtocolOptions.Enable once at boot; the listener
        // (Dhcpv4UdpListener) can still be started and stopped later whatever the flag says (see
        // IDhcpProtocolService). The flag is off by default because a second DHCP server on a network
        // that already has one causes real disruption.
        //
        // The request handler is an either/or choice: the server either answers from its own scopes and
        // leases (Ninlil.Dhcp.Server.Core) or forwards to an upstream DHCPv4 server as an RFC 3046 relay
        // agent (Ninlil.Dhcp.Server.Relay), never both. Dhcpv4RelayOptions.Enable is read here, before
        // dependency injection is built, rather than registering both and letting the last
        // registration win. Either branch must come after the DHCP repository registration, since
        // AddNinlilDhcpServerCore depends on those repositories (AddNinlilDhcpv4Relay depends on none).
        bool dhcpv4RelayEnabled =
            configuration
                .GetSection(Dhcpv4RelayOptions.SectionName)
                .Get<Dhcpv4RelayOptions>()
                ?.Enable
            ?? false;

        if (dhcpv4RelayEnabled)
            services.AddNinlilDhcpv4Relay(configuration);
        else
            services.AddNinlilDhcpServerCore(configuration);

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
            services.AddNinlilDhcpv6Relay(configuration);
        else
            services.AddNinlilDhcpv6ServerCore(configuration);

        // DHCP-to-DNS integration (registering a lease's host name as an A/AAAA record in the scope's
        // DnsZoneId) is registered in every configuration. Lease rows come only from a local engine,
        // and one protocol can be local while the other is relayed, so a relay on one side must not
        // switch it off for the other. With both relayed it sees no grants, and the expiry sweep,
        // which runs in every configuration, still lets it remove the records of leases left from
        // earlier local operation. It replaces the engine's default IDhcpDnsUpdatePolicy whichever
        // call comes first.
        services.AddNinlilDhcpDnsIntegration();

        // We're done, return the IServiceCollection instance.
        return services;
    }

    /// <summary>
    ///     This method registers the <see cref="IClientIdentityResolver" /> chain: the static address
    ///     mapping resolver, then the DHCP-lease resolver, then the ARP resolver, in every
    ///     configuration.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The <paramref name="services" /> instance.</returns>
    public static IServiceCollection AddNinlilClientIdentityResolverChain(
        this IServiceCollection services
    )
    {
        // The static-address-mapping resolver is always tried first, so an operator's explicit mapping
        // wins over ARP or DHCP data (see StaticAddressMappingClientIdentityResolver).
        //
        // The DHCP-lease resolver comes second, as the more authoritative source when this server
        // issued the lease. It is in the chain in every configuration, like the DHCP-to-DNS
        // integration that registers it: a relayed DHCPv4 side leaves its snapshot empty, and the
        // DHCPv6 side can still be local. The ARP resolver is the last fallback, since only it covers
        // a client whose lease came from another DHCP server.
        services.AddSingleton<IClientIdentityResolver>(
            provider => new CompositeClientIdentityResolver([
                provider.GetRequiredService<StaticAddressMappingClientIdentityResolver>(),
                provider.GetRequiredService<DhcpLeaseClientIdentityResolver>(),
                provider.GetRequiredService<ArpNeighborTableClientIdentityResolver>(),
            ])
        );

        // We're done, return the IServiceCollection instance.
        return services;
    }
}
