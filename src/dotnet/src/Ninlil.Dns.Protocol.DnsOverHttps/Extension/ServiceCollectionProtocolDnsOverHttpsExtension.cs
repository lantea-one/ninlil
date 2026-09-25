using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     DNS over HTTPS on its own Kestrel server. <c>Ninlil.Web</c> does not use it; it maps the
///     transport onto its own host with <see cref="EndpointRouteBuilderDnsOverHttpsExtension" />
///     instead.
/// </summary>
public static class ServiceCollectionProtocolDnsOverHttpsExtension
{
    /// <summary>
    ///     This method registers <see cref="DnsOverHttpsHostedService" />. The composition root
    ///     must also call
    ///     <c>Ninlil.Dns.Server.Core.Extension.ServiceCollectionServerCoreExtension.AddNinlilDnsServerCore</c>,
    ///     since this transport depends on <c>DnsQueryProcessingPipeline</c>.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read transport settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsProtocolDnsOverHttps(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DnsOverHttpsOptions>(
            configuration.GetSection(DnsOverHttpsOptions.SectionName)
        );

        instance.AddHostedService<DnsOverHttpsHostedService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
