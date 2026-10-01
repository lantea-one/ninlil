using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's Router Advertisement content provider.
/// </summary>
public static class ServiceCollectionDhcpServerRaExtension
{
    /// <summary>
    ///     This method binds <see cref="RaOptions" /> and registers
    ///     <see cref="RaAdvertisementProvider" /> as the <see cref="IRaAdvertisementProvider" />
    ///     that <see cref="Icmpv6RaListener" /> uses. Unlike the DHCP engines it needs no
    ///     repositories, since the content comes only from configuration.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to bind <see cref="RaOptions" /> from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpServerRa(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<RaOptions>(configuration.GetSection(RaOptions.SectionName));
        instance.AddSingleton<IRaAdvertisementProvider, RaAdvertisementProvider>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
