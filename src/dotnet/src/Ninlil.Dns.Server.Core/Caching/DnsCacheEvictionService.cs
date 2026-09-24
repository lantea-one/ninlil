using Microsoft.Extensions.Hosting;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Caching;

/// <summary>
///     This class periodically sweeps every registered <see cref="IDnsCache" /> (the recursive
///     resolver's cache and the forwarded-response cache) for expired and over-capacity entries,
///     so memory held by long-expired answers is actually freed rather than only ever being
///     noticed -- and skipped past -- the next time that exact question happens to be asked
///     again. One sweep for every cache, rather than one per cache, since the work and the
///     cadence are the same for all of them.
/// </summary>
/// <param name="caches">Every cache this service sweeps.</param>
public sealed class DnsCacheEvictionService(IEnumerable<IDnsCache> caches) : BackgroundService
{
    /// <summary>
    ///     This constant contains how often this service sweeps the caches.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly IReadOnlyList<IDnsCache> _caches = [.. caches];

    /// <summary>
    ///     This method runs one sweep over every cache. Public so a test can drive a sweep
    ///     without waiting a whole interval.
    /// </summary>
    /// <returns>How many entries were evicted across every cache.</returns>
    public int SweepOnce() => _caches.Sum(cache => cache.EvictExpiredAndOverCapacity());

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
                SweepOnce();
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
        }
    }
}
