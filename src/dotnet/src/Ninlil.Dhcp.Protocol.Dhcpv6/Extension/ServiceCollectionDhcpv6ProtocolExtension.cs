using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Control;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DHCPv6 UDP transport -- the DHCPv6 analogue of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Extension.ServiceCollectionDhcpv4ProtocolExtension</c>.
/// </summary>
public static class ServiceCollectionDhcpv6ProtocolExtension
{
    /// <summary>
    ///     This method registers <see cref="Dhcpv6UdpListener" /> as a singleton -- both under
    ///     its own concrete type and as <see cref="IDhcpProtocolService" />, so a
    ///     protocol-agnostic caller can reach it without knowing it is specifically a DHCPv6
    ///     listener -- plus <see cref="Dhcpv6ListenerHostedService" />, the thin
    ///     <see cref="Microsoft.Extensions.Hosting.IHostedService" /> wrapper that starts it
    ///     automatically at host boot only when <see cref="Dhcpv6ProtocolOptions.Enable" /> is
    ///     configured <see langword="true" /> -- see that class's own doc comment for why the
    ///     listener itself stays independently controllable for the rest of the process's life
    ///     regardless of that flag. The composition root is still expected to have already
    ///     registered a real <see cref="IDhcpv6RequestHandler" /> implementation of its own -- the
    ///     listener depends on one to decide how to answer any request it receives, and provides
    ///     no default of its own.
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
