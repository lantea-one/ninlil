using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Control;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's Router Advertisement raw ICMPv6 transport, like
///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Extension.ServiceCollectionDhcpv6ProtocolExtension</c>.
/// </summary>
public static class ServiceCollectionRaProtocolExtension
{
    /// <summary>
    ///     This method binds <see cref="RaProtocolOptions" />, registers
    ///     <see cref="Icmpv6RaListener" /> as a singleton under its own type and as
    ///     <see cref="IDhcpProtocolService" />, and adds <see cref="Icmpv6RaListenerHostedService" />,
    ///     which starts it at boot when <see cref="RaProtocolOptions.Enable" /> is
    ///     <see langword="true" />. The caller must also register an
    ///     <see cref="IRaAdvertisementProvider" />; there is no default.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read transport settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpProtocolRa(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<RaProtocolOptions>(
            configuration.GetSection(RaProtocolOptions.SectionName)
        );

        instance.AddSingleton<Icmpv6RaListener>();
        instance.AddSingleton<IDhcpProtocolService>(sp =>
            sp.GetRequiredService<Icmpv6RaListener>()
        );
        instance.AddHostedService<Icmpv6RaListenerHostedService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
