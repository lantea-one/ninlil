using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up the
///     self-update checker: <see cref="SelfUpdateOptions" />, the shared
///     <see cref="ISelfUpdateStatusCache" /> singleton, and <see cref="SelfUpdateCheckService" />
///     as a singleton that also runs as a hosted service, so <c>SelfUpdateController</c> can
///     call the same instance for a manual check.
/// </summary>
public static class ServiceCollectionSelfUpdateExtension
{
    /// <summary>
    ///     This method registers every component the self-update checker needs.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read self-update settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerSelfUpdate(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<SelfUpdateOptions>(
            configuration.GetSection(SelfUpdateOptions.SectionName)
        );

        // A named client, so IHttpClientFactory manages connection pooling and lifetime, and a
        // timeout or custom handler can be attached here.
        instance.AddHttpClient(SelfUpdateCheckService.HttpClientName);

        instance.AddSingleton<
            ICurrentAssemblyVersionProvider,
            DefaultCurrentAssemblyVersionProvider
        >();
        instance.AddSingleton<ISelfUpdateStatusCache, SelfUpdateStatusCache>();
        instance.AddSingleton<SelfUpdateCheckService>();
        instance.AddHostedService(provider =>
            provider.GetRequiredService<SelfUpdateCheckService>()
        );

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
