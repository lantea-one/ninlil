using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Control;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DHCPv4 UDP transport.
/// </summary>
public static class ServiceCollectionDhcpv4ProtocolExtension
{
    /// <summary>
    ///     This method binds <see cref="Dhcpv4ProtocolOptions" />, registers
    ///     <see cref="Dhcpv4UdpListener" /> as a singleton under its own type and as
    ///     <see cref="IDhcpProtocolService" />, and adds <see cref="Dhcpv4ListenerHostedService" />,
    ///     which starts it at boot when <see cref="Dhcpv4ProtocolOptions.Enable" /> is
    ///     <see langword="true" />. The caller must also register an
    ///     <see cref="IDhcpv4RequestHandler" />; there is no default.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read transport settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpProtocolDhcpv4(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<Dhcpv4ProtocolOptions>(
            configuration.GetSection(Dhcpv4ProtocolOptions.SectionName)
        );

        instance.AddSingleton<Dhcpv4UdpListener>();
        instance.AddSingleton<IDhcpProtocolService>(sp =>
            sp.GetRequiredService<Dhcpv4UdpListener>()
        );
        instance.AddHostedService<Dhcpv4ListenerHostedService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
