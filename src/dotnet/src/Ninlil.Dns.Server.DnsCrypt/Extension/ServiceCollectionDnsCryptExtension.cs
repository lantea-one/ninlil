using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DNSCrypt certificate management.
/// </summary>
public static class ServiceCollectionDnsCryptExtension
{
    /// <summary>
    ///     This method registers the DNSCrypt certificate store and rotation service. The
    ///     actual listener lives in <c>Ninlil.Dns.Protocol.DnsCrypt</c>, which depends on
    ///     this registration having already run.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read DNSCrypt settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerDnsCrypt(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DnsCryptOptions>(configuration.GetSection(DnsCryptOptions.SectionName));

        // IDnsCryptCertificateStore is a singleton -- its whole point (a durable, continuously
        // rotated identity) only makes sense shared across every query and every rotation
        // check, never scoped to one.
        instance.AddSingleton<IDnsCryptCertificateStore, FileDnsCryptCertificateStore>();
        instance.AddHostedService<DnsCryptCertificateRotationService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
