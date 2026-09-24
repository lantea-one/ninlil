using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Control;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's Router Advertisement raw ICMPv6 transport -- the Router Advertisement analogue of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Extension.ServiceCollectionDhcpv6ProtocolExtension</c>.
/// </summary>
public static class ServiceCollectionRaProtocolExtension
{
    /// <summary>
    ///     This method registers <see cref="Icmpv6RaListener" /> as a singleton -- both under its
    ///     own concrete type and as <see cref="IDhcpProtocolService" />, so a protocol-agnostic
    ///     caller can reach it without knowing it is specifically the Router Advertisement listener
    ///     -- plus <see cref="Icmpv6RaListenerHostedService" />, the thin
    ///     <see cref="Microsoft.Extensions.Hosting.IHostedService" /> wrapper that starts it
    ///     automatically at host boot only when <see cref="RaProtocolOptions.Enable" /> is
    ///     configured <see langword="true" /> -- see that class's own doc comment for why the
    ///     listener itself stays independently controllable for the rest of the process's life
    ///     regardless of that flag. The composition root is still expected to have already
    ///     registered a real <see cref="IRaAdvertisementProvider" /> implementation of its own --
    ///     the listener depends on one to decide what to send, and provides no default of its own.
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
