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
    ///     <see cref="IDhcpv4RelayAgentAddressProvider" />, and the real
    ///     <see cref="IDhcpv4RequestHandler" /> implementation (<see cref="Dhcpv4RelayAgent" />)
    ///     <see cref="Dhcpv4UdpListener" /> depends on.
    ///     <para>
    ///         <b>The composition root must call either this method or
    ///         <c>AddNinlilDhcpServerCore</c>, never both</b> -- see <see cref="Dhcpv4RelayOptions" />'s
    ///         own doc comment for why these two engines are mutually exclusive, and how the
    ///         composition root decides which one to register.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read this relay's own settings from.</param>
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

        // Singleton: Dhcpv4UdpListener (a process-lifetime BackgroundService) resolves this
        // exactly once, at startup, and this class owns one long-lived outbound socket of its
        // own for the whole lifetime of the process (see its own doc comment) -- unlike
        // Dhcpv4RequestHandler, this class needs no per-request dependency injection scope at
        // all, since it depends on nothing scoped (it never touches a repository or DbContext).
        instance.AddSingleton<IDhcpv4RequestHandler, Dhcpv4RelayAgent>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
