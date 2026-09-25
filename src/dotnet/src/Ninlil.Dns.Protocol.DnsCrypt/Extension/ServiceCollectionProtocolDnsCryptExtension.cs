using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     the DNSCrypt transport (UDP and TCP listeners).
/// </summary>
public static class ServiceCollectionProtocolDnsCryptExtension
{
    /// <summary>
    ///     This method registers the DNSCrypt UDP and TCP listeners as hosted services. The
    ///     composition root must also call
    ///     <c>Ninlil.Dns.Server.Core.Extension.ServiceCollectionServerCoreExtension.AddNinlilDnsServerCore</c>,
    ///     since both listeners depend on <c>DnsQueryProcessingPipeline</c>, and
    ///     <c>Ninlil.Dns.Server.DnsCrypt.Extension.ServiceCollectionDnsCryptExtension.AddNinlilDnsServerDnsCrypt</c>,
    ///     since both depend on <c>IDnsCryptCertificateStore</c> and on the
    ///     <c>DnsCryptOptions</c> binding that call makes; this method does not bind
    ///     <c>DnsCryptOptions</c> again.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsProtocolDnsCrypt(this IServiceCollection instance)
    {
        instance.AddHostedService<DnsCryptUdpListener>();
        instance.AddHostedService<DnsCryptTcpListener>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
