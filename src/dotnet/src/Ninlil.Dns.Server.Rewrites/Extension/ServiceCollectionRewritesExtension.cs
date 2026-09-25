using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Rewrites;
using Ninlil.Dns.Server.Rewrites;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up DNS
///     rewrites.
/// </summary>
public static class ServiceCollectionRewritesExtension
{
    /// <summary>
    ///     This method binds <see cref="DnsRewriteOptions" /> and registers
    ///     <see cref="IDnsRewriteEngine" />. The engine is registered even when
    ///     <see cref="DnsRewriteOptions.Enable" /> is off at startup, so that
    ///     <c>RewriteSettingsController.Patch</c> can turn rewrites on without a restart (a fresh
    ///     install starts with them off). <see cref="DnsRewriteEngine" /> checks
    ///     <see cref="DnsRewriteOptions.Enable" /> live through
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, so a disabled
    ///     engine costs one field read per query and no database access.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read rewrite settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerRewrites(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DnsRewriteOptions>(
            configuration.GetSection(DnsRewriteOptions.SectionName)
        );

        // Singleton, because the singleton DnsQueryProcessingPipeline takes it as a constructor
        // dependency. The engine reaches the scoped repository by opening a short-lived scope
        // per query through IServiceScopeFactory.
        instance.AddSingleton<IDnsRewriteEngine, DnsRewriteEngine>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
