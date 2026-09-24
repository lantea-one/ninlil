using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.ClientSchedule;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's time-of-day parental-control scheduling engine.
/// </summary>
public static class ServiceCollectionClientScheduleExtension
{
    /// <summary>
    ///     This method registers <see cref="DnsClientScheduleEngine" /> unconditionally, as a
    ///     singleton -- mirroring
    ///     <c>Ninlil.Dns.Server.Filtering.Extension.ServiceCollectionFilteringExtension</c>'s own
    ///     reasoning exactly: <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" />
    ///     (itself a singleton) takes <see cref="IDnsClientScheduleEngine" /> as a direct
    ///     constructor dependency, so a scoped registration here would be an invalid captive
    ///     dependency. This engine never holds a repository directly -- it opens its own
    ///     short-lived scope per check via the injected <c>IServiceScopeFactory</c>, exactly like
    ///     <c>DnsFilterEngine</c> already does.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerClientSchedule(
        this IServiceCollection instance
    )
    {
        instance.AddSingleton<IDnsClientScheduleEngine, DnsClientScheduleEngine>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
