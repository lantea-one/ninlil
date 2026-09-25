using Microsoft.Extensions.DependencyInjection;
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
    ///     a scoped registration would be a captive dependency. The engine opens a short-lived
    ///     scope for its repositories on each check.
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
