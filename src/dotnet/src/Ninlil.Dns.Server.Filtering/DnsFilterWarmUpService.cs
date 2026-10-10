using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This class loads the filter data into memory when the host starts, so the first DNS query
///     does not wait for it: the client directory, the filter configuration and the entries of
///     every list a group or client uses (a 100,000-entry list takes a fraction of a second to
///     seconds to load, more on MongoDB). It runs in the background and does not delay the host's
///     readiness: queries that arrive earlier wait for the same loads the cache would start
///     anyway. It does nothing while <see cref="DnsFilterOptions.Enable" /> is off at startup; the
///     first query after filtering is turned on loads the data then. A failed load is logged and
///     left to the first query, which retries.
/// </summary>
/// <param name="cache">The cache to warm.</param>
/// <param name="clientDirectory">The client directory the filter engine looks clients up in.</param>
/// <param name="options">The <see cref="DnsFilterOptions.Enable" /> switch, read at startup.</param>
/// <param name="logger">The logger the outcome is recorded to.</param>
public sealed class DnsFilterWarmUpService(
    DnsFilterConfigurationCache cache,
    DnsClientDirectory clientDirectory,
    IOptionsMonitor<DnsFilterOptions> options,
    ILogger<DnsFilterWarmUpService> logger
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Return control to the host at once; the loads below must not hold up startup.
        await Task.Yield();

        if (!options.CurrentValue.Enable)
            return;

        try
        {
            await clientDirectory.WarmUpAsync(stoppingToken);
            int lists = await cache.WarmUpAsync(stoppingToken);

            logger.LogInformation(
                "Loaded the filter configuration and {Count} filter lists.",
                lists
            );
        }
        catch (OperationCanceledException)
        {
            // The host is stopping.
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not load the filter data at startup; the first query will load it."
            );
        }
    }
}
