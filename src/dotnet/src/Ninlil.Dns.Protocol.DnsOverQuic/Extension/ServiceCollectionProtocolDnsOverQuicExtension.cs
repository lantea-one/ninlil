using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverQuic.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     the DNS-over-QUIC transport.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public static class ServiceCollectionProtocolDnsOverQuicExtension
{
    /// <summary>
    ///     This method registers the DNS-over-QUIC listener as a hosted service. The composition
    ///     root must also call
    ///     <c>Ninlil.Dns.Server.Core.Extension.ServiceCollectionServerCoreExtension.AddNinlilDnsServerCore</c>,
    ///     since the listener depends on <c>DnsQueryProcessingPipeline</c>, and register
    ///     <c>IZoneTransferService</c> for zone transfers over QUIC.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read transport settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsProtocolDnsOverQuic(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DnsOverQuicOptions>(
            configuration.GetSection(DnsOverQuicOptions.SectionName)
        );

        instance.AddHostedService<DnsOverQuicListener>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
