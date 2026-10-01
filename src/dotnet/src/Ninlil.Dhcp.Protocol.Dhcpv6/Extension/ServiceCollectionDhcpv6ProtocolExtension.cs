using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Control;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DHCPv6 UDP transport, like
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Extension.ServiceCollectionDhcpv4ProtocolExtension</c>.
/// </summary>
public static class ServiceCollectionDhcpv6ProtocolExtension
{
    /// <summary>
    ///     This method binds <see cref="Dhcpv6ProtocolOptions" />, registers
    ///     <see cref="Dhcpv6UdpListener" /> as a singleton under its own type and as
    ///     <see cref="IDhcpProtocolService" />, and adds <see cref="Dhcpv6ListenerHostedService" />,
    ///     which starts it at boot when <see cref="Dhcpv6ProtocolOptions.Enable" /> is
    ///     <see langword="true" />. The caller must also register an
    ///     <see cref="IDhcpv6RequestHandler" />; there is no default.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read transport settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpProtocolDhcpv6(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<Dhcpv6ProtocolOptions>(
            configuration.GetSection(Dhcpv6ProtocolOptions.SectionName)
        );

        instance.AddSingleton<Dhcpv6UdpListener>();
        instance.AddSingleton<IDhcpProtocolService>(sp =>
            sp.GetRequiredService<Dhcpv6UdpListener>()
        );
        instance.AddHostedService<Dhcpv6ListenerHostedService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
