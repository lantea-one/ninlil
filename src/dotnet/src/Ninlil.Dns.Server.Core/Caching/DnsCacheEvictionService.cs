using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Caching;

/// <summary>
///     This class sweeps every registered <see cref="IDnsCache" /> once a minute for expired and
///     over-capacity entries. Without it, an expired entry is only noticed when the same
///     question is asked again, so its memory could be held indefinitely.
/// </summary>
/// <param name="caches">Every cache this service sweeps.</param>
/// <param name="logger">The logger this service records a failed sweep to.</param>
/// <param name="sweepInterval">How often to sweep the caches. Defaults to one minute.</param>
public sealed class DnsCacheEvictionService(
    IEnumerable<IDnsCache> caches,
    ILogger<DnsCacheEvictionService> logger,
    TimeSpan? sweepInterval = null
) : BackgroundService
{
    private readonly IReadOnlyList<IDnsCache> _caches = [.. caches];
    private readonly TimeSpan _sweepInterval = sweepInterval ?? TimeSpan.FromMinutes(1);

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
                await Task.Delay(_sweepInterval, stoppingToken);
                SweepOnce();
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
            catch (Exception exception)
            {
                // A failed sweep must not end this loop: an unhandled exception would stop the
                // host (BackgroundServiceExceptionBehavior.StopHost), and the caches would
                // never be swept again. The next sweep tries again.
                logger.LogError(exception, "Sweeping the DNS caches failed.");
            }
        }
    }
}
