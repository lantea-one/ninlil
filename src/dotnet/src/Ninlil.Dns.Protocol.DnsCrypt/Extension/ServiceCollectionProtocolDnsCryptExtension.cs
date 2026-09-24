using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DNSCrypt transport.
/// </summary>
public static class ServiceCollectionProtocolDnsCryptExtension
{
    /// <summary>
    ///     This method registers the DNSCrypt UDP and TCP listeners. The composition root is
    ///     still expected to have already called
    ///     <c>Ninlil.Dns.Server.Core.Extension.ServiceCollectionServerCoreExtension.AddNinlilDnsServerCore</c>,
    ///     since both listeners depend on <c>DnsQueryProcessingPipeline</c>, and
    ///     <c>Ninlil.Dns.Server.DnsCrypt.Extension.ServiceCollectionDnsCryptExtension.AddNinlilDnsServerDnsCrypt</c>,
    ///     since both depend on <c>IDnsCryptCertificateStore</c> and the same
    ///     <c>DnsCryptOptions</c> binding that call already configures -- this method
    ///     deliberately does not bind <c>DnsCryptOptions</c> a second time.
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
