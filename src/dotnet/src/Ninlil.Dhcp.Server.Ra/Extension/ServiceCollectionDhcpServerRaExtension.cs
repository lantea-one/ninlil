using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's real Router Advertisement content engine.
/// </summary>
public static class ServiceCollectionDhcpServerRaExtension
{
    /// <summary>
    ///     This method registers <see cref="RaOptions" /> and the real
    ///     <see cref="IRaAdvertisementProvider" /> implementation (<see cref="RaAdvertisementProvider" />)
    ///     <see cref="Icmpv6RaListener" /> depends on. Unlike the DHCPv4/DHCPv6 engines, this
    ///     needs no repository registration of its own first -- Router Advertisement content comes
    ///     entirely from configuration, with no persisted lease/scope state at all.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read this server's own Router Advertisement content from.</param>
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
