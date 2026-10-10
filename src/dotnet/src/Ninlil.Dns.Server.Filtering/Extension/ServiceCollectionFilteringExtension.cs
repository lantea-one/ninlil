using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ninlil.Dns.Server.Core.ClientIdentity;
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
    ///     This method binds <see cref="DnsFilterOptions" /> and registers the filter engine and the
    ///     subscribed-list refresh service. Both are registered even when
    ///     <see cref="DnsFilterOptions.Enable" /> is off at startup, so that
    ///     <c>FilteringSettingsController.Patch</c> can turn filtering on without a restart (a
    ///     fresh install starts with it off). <see cref="DnsFilterEngine" /> checks
    ///     <see cref="DnsFilterOptions.Enable" /> live through
    ///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, so a disabled
    ///     engine costs one field read per check and no database access. It also registers the
    ///     <see cref="DnsFilterConfigurationCache" /> and the <see cref="DnsClientDirectory" /> the
    ///     engine reads; both need an <c>IEntityChangeNotifier</c>, which the storage extensions
    ///     register.
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

        // Singleton, because the singleton DnsQueryProcessingPipeline takes it as a constructor
        // dependency. The engine reads the singleton configuration cache and client directory,
        // which open a short-lived scope only when they load. The directory uses TryAdd so another
        // extension that needs it can register the same singleton.
        instance.AddSingleton<DnsFilterConfigurationCache>();
        instance.TryAddSingleton<DnsClientDirectory>();
        instance.AddSingleton<IDnsFilterEngine, DnsFilterEngine>();
        instance.AddHostedService<DnsFilterListExternalRefreshService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
