using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     the query log (the subscriber that writes it and the service that prunes it).
/// </summary>
public static class ServiceCollectionQueryLogExtension
{
    /// <summary>
    ///     This method binds <see cref="QueryLoggingOptions" /> and registers
    ///     <see cref="DnsQueryLogCleanupService" />, which runs even while logging is off so
    ///     existing entries still age out. <see cref="DnsQueryLogSubscriber" />, the
    ///     <see cref="DnsQueryLogQueue" /> it fills and the <see cref="DnsQueryLogWriterService" />
    ///     that drains it are registered only when <see cref="QueryLoggingOptions.Enable" /> is
    ///     <see langword="true" /> at startup.
    ///     The composition root must register an
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IDnsQueryLogRepository" />.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read query logging settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerQueryLog(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<QueryLoggingOptions>(
            configuration.GetSection(QueryLoggingOptions.SectionName)
        );

        QueryLoggingOptions options =
            configuration.GetSection(QueryLoggingOptions.SectionName).Get<QueryLoggingOptions>()
            ?? new QueryLoggingOptions();

        if (options.Enable)
        {
            instance.AddSingleton<DnsQueryLogQueue>();
            instance.AddHostedService<DnsQueryLogWriterService>();
            instance.AddSingleton<
                IDnsServerEventSubscriber<DnsQueryAnsweredEvent>,
                DnsQueryLogSubscriber
            >();
        }

        instance.AddHostedService<DnsQueryLogCleanupService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
