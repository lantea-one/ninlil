using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Tsig.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Extension;

/// <summary>
///     This class provides the <see cref="IServiceCollection" /> extension that registers the
///     TSIG engine (<see cref="ITsigService" />) and its options.
/// </summary>
public static class ServiceCollectionTsigExtension
{
    /// <summary>
    ///     This method registers <see cref="ITsigService" /> (scoped),
    ///     <see cref="TsigSecretProtector" /> and <see cref="TsigServiceOptions" />. The caller
    ///     must also register an
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
        // Data Protection encrypts TSIG shared secrets at rest (see TsigSecretProtector).
        instance.AddDataProtection();

        instance.Configure<TsigServiceOptions>(
            configuration.GetSection(TsigServiceOptions.SectionName)
        );

        instance.AddSingleton<TsigSecretProtector>();
        instance.AddScoped<ITsigService, TsigService>();

        return instance;
    }
}
