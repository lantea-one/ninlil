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
    ///     listeners live in <c>Ninlil.Dns.Protocol.DnsCrypt</c> and need these registrations.
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

        // A singleton, because every query and every rotation check must share one identity.
        instance.AddSingleton<IDnsCryptCertificateStore, FileDnsCryptCertificateStore>();
        instance.AddHostedService<DnsCryptCertificateRotationService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
