using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DNS-over-HTTPS transport.
/// </summary>
public static class ServiceCollectionProtocolDnsOverHttpsExtension
{
    /// <summary>
    ///     This method registers the DNS-over-HTTPS hosted service. The composition root is
    ///     still expected to have already called
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
