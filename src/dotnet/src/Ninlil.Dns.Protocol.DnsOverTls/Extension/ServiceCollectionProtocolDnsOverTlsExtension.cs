using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DNS-over-TLS transport.
/// </summary>
public static class ServiceCollectionProtocolDnsOverTlsExtension
{
    /// <summary>
    ///     This method registers the DNS-over-TLS listener. The composition root is still
    ///     expected to have already called
    ///     <c>Ninlil.Dns.Server.Core.Extension.ServiceCollectionServerCoreExtension.AddNinlilDnsServerCore</c>,
    ///     since this transport depends on <c>DnsQueryProcessingPipeline</c>.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read transport settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsProtocolDnsOverTls(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DnsOverTlsOptions>(
            configuration.GetSection(DnsOverTlsOptions.SectionName)
        );

        instance.AddHostedService<DnsOverTlsListener>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
