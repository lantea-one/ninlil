using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Rewrites;
using Ninlil.Dns.Server.Rewrites;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's Phase 5h-3 DNS rewrites.
/// </summary>
public static class ServiceCollectionRewritesExtension
{
    /// <summary>
    ///     This method binds <see cref="DnsRewriteOptions" /> and registers
    ///     <see cref="IDnsRewriteEngine" /> unconditionally, regardless of
    ///     <see cref="DnsRewriteOptions.Enable" />'s own value at process startup -- a
    ///     <c>RewriteSettingsController.Patch</c> write toggling rewrites on at runtime needs a
    ///     real, already-constructed <see cref="IDnsRewriteEngine" /> for
    ///     <c>DnsQueryProcessingPipeline</c> to actually consult; registering it only when already
    ///     enabled at startup (this method's own original shape) would mean a fresh install --
    ///     which starts with rewrites off by design -- could never turn them on live at all, the
    ///     same structural gap Phase 5o-3 found and fixed for DNS-over-HTTPS's own route mapping.
    ///     <see cref="DnsRewriteEngine" /> itself now carries the live
    ///     <see cref="DnsRewriteOptions.Enable" /> check internally (via
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />), so a disabled
    ///     engine costs one field read per check, never a database round trip.
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

        // Singleton, not scoped: DnsQueryProcessingPipeline (itself a singleton) takes
        // IDnsRewriteEngine as a direct constructor dependency, so a scoped registration here
        // would be an invalid captive dependency -- the same reasoning
        // ServiceCollectionFilteringExtension already documents for IDnsFilterEngine.
        // DnsRewriteEngine itself never holds a repository directly -- it opens its own
        // short-lived scope per check via the injected IServiceScopeFactory.
        instance.AddSingleton<IDnsRewriteEngine, DnsRewriteEngine>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
