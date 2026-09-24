using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's configuration-preset engine.
/// </summary>
public static class ServiceCollectionPresetsExtension
{
    /// <summary>
    ///     This method registers <see cref="PresetFilterListApplier" /> and
    ///     <see cref="PresetApplier" />. The composition root is still expected to have already
    ///     registered <c>IDnsClientGroupRepository</c>, <c>IDnsFilterListRepository</c>,
    ///     <c>IDnsClientGroupListAssignmentRepository</c>, and <c>IUnitOfWork</c> implementations
    ///     -- the same repositories <c>Server.Filtering</c> already depends on.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerPresets(this IServiceCollection instance)
    {
        instance.AddScoped<PresetFilterListApplier>();
        instance.AddScoped<PresetApplier>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
