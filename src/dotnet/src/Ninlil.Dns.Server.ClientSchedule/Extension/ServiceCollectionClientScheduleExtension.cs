using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.ClientSchedule;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     the parental-control engine (client and group pauses and time-of-day schedules).
/// </summary>
public static class ServiceCollectionClientScheduleExtension
{
    /// <summary>
    ///     This method registers <see cref="DnsClientScheduleEngine" /> as a singleton, as the
    ///     filter engine is: <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" />
    ///     is a singleton that takes <see cref="IDnsClientScheduleEngine" /> in its constructor, so
    ///     a scoped registration would be a captive dependency. The engine reads the singleton
    ///     <see cref="DnsClientScheduleCache" /> and <see cref="DnsClientDirectory" />, which open a
    ///     short-lived scope only when they load; both need an <c>IEntityChangeNotifier</c>, which
    ///     the storage extensions register.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerClientSchedule(
        this IServiceCollection instance
    )
    {
        instance.AddSingleton<DnsClientScheduleCache>();

        // TryAdd, because the filtering and rewrite extensions register the same singleton.
        instance.TryAddSingleton<DnsClientDirectory>();
        instance.AddSingleton<IDnsClientScheduleEngine, DnsClientScheduleEngine>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
