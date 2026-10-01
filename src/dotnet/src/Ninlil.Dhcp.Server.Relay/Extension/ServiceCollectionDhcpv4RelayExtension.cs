using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Server.Relay.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up Ninlil's
///     DHCPv4 relay agent.
/// </summary>
public static class ServiceCollectionDhcpv4RelayExtension
{
    /// <summary>
    ///     This method registers <see cref="Dhcpv4RelayOptions" />,
    ///     <see cref="IDhcpv4RelayAgentAddressProvider" /> and <see cref="Dhcpv4RelayAgent" /> as
    ///     the <see cref="IDhcpv4RequestHandler" /> that <see cref="Dhcpv4UdpListener" /> uses.
    ///     <para>
    ///         <b>Call either this method or <c>AddNinlilDhcpServerCore</c>, not both.</b> See
    ///         <see cref="Dhcpv4RelayOptions" /> for how <c>Program.cs</c> chooses.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration the relay settings are read from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpv4Relay(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<Dhcpv4RelayOptions>(
            configuration.GetSection(Dhcpv4RelayOptions.SectionName)
        );

        instance.AddSingleton<IDhcpv4RelayAgentAddressProvider, Dhcpv4RelayAgentAddressProvider>();

        // Singleton: the relay holds one outbound socket for the life of the process and uses
        // no scoped services, so unlike Dhcpv4RequestHandler it needs no per-request scope.
        instance.AddSingleton<IDhcpv4RequestHandler, Dhcpv4RelayAgent>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
