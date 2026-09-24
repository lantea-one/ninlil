using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Caching;

/// <summary>
///     This class periodically finds <see cref="IRecursiveCache" /> entries that are both
///     nearing TTL expiry and still genuinely being queried, and proactively refreshes them via
///     <see cref="IIterativeResolutionEngine.PrefetchAsync" /> -- so a popular name's own answer
///     never actually goes cold from this resolver's own perspective, per
///     <see cref="RecursiveServerOptions.EnablePrefetch" />'s own doc comment. Registered
///     unconditionally, like every other options-gated hosted service in this project, with its
///     own live <see cref="RecursiveServerOptions.EnablePrefetch" /> check inside the sweep loop
///     rather than a conditional DI registration -- the same hot-reload-safe shape this project's
///     other live-toggleable settings already establish elsewhere, since a snapshot-time
///     registration decision would never see a later live toggle.
/// </summary>
/// <param name="cache">The cache this service finds prefetch candidates in.</param>
/// <param name="engine">The engine this service actually re-resolves a candidate through.</param>
/// <param name="options">
///     This service's own live options, read fresh on every sweep via
///     <see cref="IOptionsMonitor{TOptions}" /> -- see <see cref="Resolution.RecursiveQueryResolver" />'s
///     own doc comment for why a background service like this one, registered as a singleton for
///     its whole process lifetime, cannot use the usual snapshotted <see cref="IOptions{TOptions}" />
///     instead.
/// </param>
/// <param name="logger">This service's own logger.</param>
public sealed class RecursiveCachePrefetchService(
    IRecursiveCache cache,
    IIterativeResolutionEngine engine,
    IOptionsMonitor<RecursiveServerOptions> options,
    ILogger<RecursiveCachePrefetchService> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant contains how often this service sweeps the cache for prefetch
    ///     candidates. Deliberately much shorter than <see cref="RecursiveCacheEvictionService" />'s
    ///     own one-minute sweep interval: <see cref="RecursiveServerOptions.PrefetchWindow" />'s
    ///     own default is only 10 seconds, so a sweep cadence anywhere near a minute would miss
    ///     most entries during the one brief window they actually qualify in. Five seconds
    ///     guarantees every entry is checked at least once (typically twice) while it sits inside
    ///     even that short a window.
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

                // Every candidate refreshes concurrently and independently -- one candidate's
                // upstream failure (a name server that has genuinely gone away since it was first
                // cached, say) must never block or fail the others in the same sweep.
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
    ///     This method prefetches a single candidate, swallowing (but logging) any failure -- a
    ///     stale prefetch attempt is never worse than not prefetching at all, since the original,
    ///     still-cached entry is untouched until <see cref="IIterativeResolutionEngine.PrefetchAsync" />
    ///     actually succeeds and calls back into <see cref="IRecursiveCache.StorePositive" />.
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
            // Expected during a graceful shutdown -- let ExecuteAsync's own catch handle it.
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
