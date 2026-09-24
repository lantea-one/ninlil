using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's persistent query log.
/// </summary>
public static class ServiceCollectionQueryLogExtension
{
    /// <summary>
    ///     This method always binds <see cref="QueryLoggingOptions" /> and always registers
    ///     <see cref="DnsQueryLogCleanupService" /> -- the cleanup sweep must keep running even
    ///     while logging itself is turned off, so any data already on disk from before it was
    ///     disabled still ages out on schedule. <see cref="DnsQueryLogSubscriber" /> is only
    ///     registered when <see cref="QueryLoggingOptions.Enable" /> is <see langword="true" />,
    ///     the same "only register the thing that does the work when the feature is actually on"
    ///     pattern <c>Server.Rewrites</c>'s own extension already establishes for
    ///     <c>IDnsRewriteEngine</c> -- the composition root is still expected to have already
    ///     registered an <see cref="Ninlil.Dns.Data.Abstractions.Repository.IDnsQueryLogRepository" />
    ///     implementation.
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
            instance.AddSingleton<
                IDnsServerEventSubscriber<DnsQueryAnsweredEvent>,
                DnsQueryLogSubscriber
            >();

        instance.AddHostedService<DnsQueryLogCleanupService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
