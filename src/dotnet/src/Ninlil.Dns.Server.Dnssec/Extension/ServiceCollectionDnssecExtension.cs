using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Extension;

/// <summary>
///     This class provides the <see cref="IServiceCollection" /> extension that registers DNSSEC
///     key management. The algorithm classes and codecs in this project are static and need no
///     registration.
/// </summary>
public static class ServiceCollectionDnssecExtension
{
    /// <summary>
    ///     This method registers <see cref="IDnssecKeyManagementService" /> (scoped), the singleton
    ///     <see cref="DnssecZoneKeyGate" /> and Data
    ///     Protection. The caller must also register an
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IDnssecKeyRepository" /> and an
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IUnitOfWork" />.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">
    ///     The application configuration to read <see cref="DnssecAlgorithmPolicyOptions" />
    ///     from. When <see langword="null" />, the policy is not bound and
    ///     <see cref="DnssecKeyManagementService" /> allows every supported algorithm.
    /// </param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerDnssec(
        this IServiceCollection instance,
        IConfiguration configuration = null
    )
    {
        // Data Protection encrypts private keys at rest (see DnssecKeyManagementService).
        instance.AddDataProtection();

        if (configuration is not null)
            instance.Configure<DnssecAlgorithmPolicyOptions>(
                configuration.GetSection(DnssecAlgorithmPolicyOptions.SectionName)
            );

        // One gate for the whole process: scoped services and the rollover background service
        // must share it to serialise a zone's key changes.
        instance.AddSingleton<DnssecZoneKeyGate>();
        instance.AddScoped<IDnssecKeyManagementService, DnssecKeyManagementService>();

        return instance;
    }
}
