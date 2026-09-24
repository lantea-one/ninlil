using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Tsig.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's TSIG engine.
/// </summary>
public static class ServiceCollectionTsigExtension
{
    /// <summary>
    ///     This method registers <see cref="ITsigService" />. The composition root is still
    ///     expected to have already registered an
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.ITsigKeyRepository" />
    ///     implementation.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read TSIG settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerTsig(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        // Data Protection backs the encryption of a TSIG key's shared secret at rest, the same
        // mechanism Ninlil.Dns.Identity already uses for TOTP authenticator keys.
        instance.AddDataProtection();

        instance.Configure<TsigServiceOptions>(
            configuration.GetSection(TsigServiceOptions.SectionName)
        );

        instance.AddSingleton<TsigSecretProtector>();
        instance.AddScoped<ITsigService, TsigService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
