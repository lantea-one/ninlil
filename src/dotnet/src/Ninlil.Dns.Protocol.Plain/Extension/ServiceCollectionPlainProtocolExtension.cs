using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     plain DNS over UDP and TCP (port 53 by default).
/// </summary>
public static class ServiceCollectionPlainProtocolExtension
{
    /// <summary>
    ///     This method registers the UDP and TCP listeners as hosted services. The composition
    ///     root must also call
    ///     <c>Ninlil.Dns.Server.Core.Extension.ServiceCollectionServerCoreExtension.AddNinlilDnsServerCore</c>,
    ///     since both listeners depend on <c>DnsQueryProcessingPipeline</c>, and
    ///     <c>Ninlil.Dns.Server.Authoritative.Extension.ServiceCollectionAuthoritativeExtension.AddNinlilDnsServerAuthoritative</c>,
    ///     since the TCP listener depends on <c>IZoneTransferService</c>.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read transport settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsProtocolPlain(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<PlainProtocolOptions>(
            configuration.GetSection(PlainProtocolOptions.SectionName)
        );

        instance.AddHostedService<UdpDnsListener>();
        instance.AddHostedService<TcpDnsListener>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
