using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Filtering;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's block/allow-list filtering.
/// </summary>
public static class ServiceCollectionFilteringExtension
{
    /// <summary>
    ///     This method registers the filter engine and its external-list refresh service
    ///     unconditionally, regardless of <see cref="DnsFilterOptions.Enable" />'s own value at
    ///     process startup -- a <c>FilteringSettingsController.Patch</c> write toggling filtering
    ///     on at runtime needs a real, already-constructed <see cref="IDnsFilterEngine" /> for
    ///     <c>DnsQueryProcessingPipeline</c> to actually consult; registering it only when already
    ///     enabled at startup would mean a fresh install -- which starts with filtering off by
    ///     design -- could never turn it on live at all, the same structural gap this project's
    ///     own DNS-over-HTTPS route-mapping registration had to be fixed for.
    ///     <see cref="DnsFilterEngine" /> itself now carries the live <see cref="DnsFilterOptions.Enable" />
    ///     check internally (via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />),
    ///     so a disabled engine costs one field read per check, never a database round trip.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read filtering settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerFiltering(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DnsFilterOptions>(
            configuration.GetSection(DnsFilterOptions.SectionName)
        );

        instance.AddHttpClient();

        // Singleton, not scoped: DnsQueryProcessingPipeline (itself a singleton) takes
        // IDnsFilterEngine as a direct constructor dependency, so a scoped registration here
        // would be an invalid captive dependency. DnsFilterEngine itself never holds a
        // repository directly -- it opens its own short-lived scope per check via the injected
        // IServiceScopeFactory, the same pattern CloudDnsZoneSyncService already establishes.
        instance.AddSingleton<IDnsFilterEngine, DnsFilterEngine>();
        instance.AddHostedService<DnsFilterListExternalRefreshService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
