using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Relay.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up Ninlil's
///     DHCPv6 relay agent -- the DHCPv6 analogue of <see cref="ServiceCollectionDhcpv4RelayExtension" />.
/// </summary>
public static class ServiceCollectionDhcpv6RelayExtension
{
    /// <summary>
    ///     This method registers <see cref="Dhcpv6RelayOptions" />,
    ///     <see cref="IDhcpv6RelayAgentAddressProvider" />, and the real
    ///     <see cref="IDhcpv6RequestHandler" />/<see cref="IDhcpv6RelayMessageHandler" />
    ///     implementation (<see cref="Dhcpv6RelayAgent" />) <see cref="Dhcpv6UdpListener" />
    ///     depends on -- registered once as its own concrete type, then wired to both interfaces
    ///     (and to <see cref="IHostedService" /> itself, so its own background receive loop
    ///     actually runs) against that exact same singleton instance, the same "one instance,
    ///     several registrations" shape <c>Ninlil.Dhcp.Server.Core.Extension
    ///     .ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpv6ServerCore</c> uses for
    ///     <c>Dhcpv6RequestHandler</c>.
    ///     <para>
    ///         <b>The composition root must call either this method or
    ///         <c>AddNinlilDhcpv6ServerCore</c>, never both</b> -- see <see cref="Dhcpv6RelayOptions" />'s
    ///         own doc comment for why these two engines are mutually exclusive.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read this relay's own settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpv6Relay(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<Dhcpv6RelayOptions>(
            configuration.GetSection(Dhcpv6RelayOptions.SectionName)
        );

        instance.AddSingleton<IDhcpv6RelayAgentAddressProvider, Dhcpv6RelayAgentAddressProvider>();

        instance.AddSingleton<Dhcpv6RelayAgent>();
        instance.AddSingleton<IDhcpv6RequestHandler>(sp =>
            sp.GetRequiredService<Dhcpv6RelayAgent>()
        );
        instance.AddSingleton<IDhcpv6RelayMessageHandler>(sp =>
            sp.GetRequiredService<Dhcpv6RelayAgent>()
        );

        // This relay's own background receive loop (see Dhcpv6RelayAgent's own doc comment for
        // why it needs one at all, unlike the DHCPv4 relay agent) only actually runs once
        // registered as a hosted service too -- resolving the exact same singleton instance the
        // two registrations above already share, never a second, independently constructed one.
        instance.AddHostedService(sp => sp.GetRequiredService<Dhcpv6RelayAgent>());

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
