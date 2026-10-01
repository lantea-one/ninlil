using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Relay.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up Ninlil's
///     DHCPv6 relay agent, the counterpart of <see cref="ServiceCollectionDhcpv4RelayExtension" />.
/// </summary>
public static class ServiceCollectionDhcpv6RelayExtension
{
    /// <summary>
    ///     This method registers <see cref="Dhcpv6RelayOptions" />,
    ///     <see cref="IDhcpv6RelayAgentAddressProvider" /> and <see cref="Dhcpv6RelayAgent" />. The
    ///     relay is one singleton exposed as <see cref="IDhcpv6RequestHandler" />,
    ///     <see cref="IDhcpv6RelayMessageHandler" /> (both used by <see cref="Dhcpv6UdpListener" />)
    ///     and <see cref="IHostedService" />, which runs its receive loop.
    ///     <para>
    ///         <b>Call either this method or <c>AddNinlilDhcpv6ServerCore</c>, not both.</b> See
    ///         <see cref="Dhcpv6RelayOptions" /> for how <c>Program.cs</c> chooses.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration the relay settings are read from.</param>
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

        // Register the same instance as a hosted service so its receive loop runs (see
        // Dhcpv6RelayAgent). A second instance would open a second socket.
        instance.AddHostedService(sp => sp.GetRequiredService<Dhcpv6RelayAgent>());

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
