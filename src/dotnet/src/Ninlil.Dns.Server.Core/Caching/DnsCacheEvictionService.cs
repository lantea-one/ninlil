using Microsoft.Extensions.Hosting;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Caching;

/// <summary>
///     This class sweeps every registered <see cref="IDnsCache" /> once a minute for expired and
///     over-capacity entries. Without it, an expired entry is only noticed when the same
///     question is asked again, so its memory could be held indefinitely.
/// </summary>
/// <param name="caches">Every cache this service sweeps.</param>
public sealed class DnsCacheEvictionService(IEnumerable<IDnsCache> caches) : BackgroundService
{
    /// <summary>
    ///     This field contains how often this service sweeps the caches.
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
