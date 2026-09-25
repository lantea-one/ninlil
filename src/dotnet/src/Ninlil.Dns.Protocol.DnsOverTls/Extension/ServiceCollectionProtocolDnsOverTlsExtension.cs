using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     the DNS-over-TLS transport.
/// </summary>
public static class ServiceCollectionProtocolDnsOverTlsExtension
{
    /// <summary>
    ///     This method registers the DNS-over-TLS listener as a hosted service. The composition
    ///     root must also call
    ///     <c>Ninlil.Dns.Server.Core.Extension.ServiceCollectionServerCoreExtension.AddNinlilDnsServerCore</c>,
    ///     since the listener depends on <c>DnsQueryProcessingPipeline</c>, and register
    ///     <c>IZoneTransferService</c> for zone transfers over TLS.
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
