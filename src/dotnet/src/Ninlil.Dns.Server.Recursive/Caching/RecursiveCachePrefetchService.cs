using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Caching;

/// <summary>
///     This class refreshes <see cref="IRecursiveCache" /> entries that are near expiry and still
///     being looked up, through <see cref="IIterativeResolutionEngine.PrefetchAsync" />, so a
///     popular name is refreshed before a client waits for it (see
///     <see cref="RecursiveServerOptions.EnablePrefetch" />). Every five seconds it takes the
///     candidates from <see cref="IRecursiveCache.GetPrefetchCandidates" /> and refreshes them
///     all in parallel. It is always registered and checks
///     <see cref="RecursiveServerOptions.EnablePrefetch" /> on every sweep, so the setting can be
///     changed without a restart.
/// </summary>
/// <param name="cache">The cache this service finds prefetch candidates in.</param>
/// <param name="engine">The engine this service re-resolves a candidate through.</param>
/// <param name="options">
///     The prefetch settings, read on every sweep through
///     <see cref="IOptionsMonitor{TOptions}" />.
/// </param>
/// <param name="logger">The logger this service records failed prefetches to.</param>
public sealed class RecursiveCachePrefetchService(
    IRecursiveCache cache,
    IIterativeResolutionEngine engine,
    IOptionsMonitor<RecursiveServerOptions> options,
    ILogger<RecursiveCachePrefetchService> logger
) : BackgroundService
{
    /// <summary>
    ///     This field contains how often this service looks for prefetch candidates. It must be
    ///     shorter than <see cref="RecursiveServerOptions.PrefetchWindow" /> (10 seconds by
    ///     default), or entries would pass through the window between sweeps; five seconds checks
    ///     each entry at least once inside the default window.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SweepInterval, stoppingToken);

                RecursiveServerOptions currentOptions = options.CurrentValue;

                if (!currentOptions.EnablePrefetch)
                    continue;

                IReadOnlyList<(
                    DnsName Name,
                    DnsResourceRecordTypeEnum Type,
                    DnsClassEnum Class
                )> candidates = cache.GetPrefetchCandidates(
                    currentOptions.PrefetchWindow,
                    currentOptions.PrefetchRecentAccessWindow
                );

                if (candidates.Count == 0)
                    continue;

                // Candidates refresh in parallel, and one failure must not stop the others.
                await Task.WhenAll(
                    candidates.Select(candidate =>
                        PrefetchOneAsync(candidate, currentOptions, stoppingToken)
                    )
                );
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
        }
    }

    /// <summary>
    ///     This method prefetches one candidate, logging and swallowing any failure other than
    ///     cancellation. A failed prefetch does no harm: the cached entry stays until the engine
    ///     stores a new answer.
    /// </summary>
    private async Task PrefetchOneAsync(
        (DnsName Name, DnsResourceRecordTypeEnum Type, DnsClassEnum Class) candidate,
        RecursiveServerOptions currentOptions,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await engine.PrefetchAsync(
                candidate.Name,
                candidate.Type,
                candidate.Class,
                currentOptions.EnableDnssecValidation,
                stoppingToken
            );
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown; ExecuteAsync handles it.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Active cache prefetch failed for {Name} {Type} {Class} -- the existing cache entry is untouched.",
                candidate.Name,
                candidate.Type,
                candidate.Class
            );
        }
    }
}
