using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DNSSEC engine.
/// </summary>
public static class ServiceCollectionDnssecExtension
{
    /// <summary>
    ///     This method registers <see cref="IDnssecKeyManagementService" />. The composition root
    ///     is still expected to have already registered an
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IDnssecKeyRepository" />
    ///     implementation.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">
    ///     The application configuration to read <see cref="DnssecAlgorithmPolicyOptions" />
    ///     from. <see langword="null" /> (the default, so every pre-existing caller keeps
    ///     compiling unchanged) means the algorithm policy is left entirely unconfigured, which
    ///     <see cref="DnssecKeyManagementService" /> itself already treats as "no restriction."
    /// </param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerDnssec(
        this IServiceCollection instance,
        IConfiguration configuration = null
    )
    {
        // Data Protection backs the encryption of a key's private key material at rest, the same
        // mechanism Ninlil.Dns.Server.Tsig already uses for a TSIG shared secret.
        instance.AddDataProtection();

        if (configuration is not null)
            instance.Configure<DnssecAlgorithmPolicyOptions>(
                configuration.GetSection(DnssecAlgorithmPolicyOptions.SectionName)
            );

        instance.AddScoped<IDnssecKeyManagementService, DnssecKeyManagementService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
