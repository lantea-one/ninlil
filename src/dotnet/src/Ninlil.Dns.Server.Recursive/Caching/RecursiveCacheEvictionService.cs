using Microsoft.Extensions.Hosting;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Caching;

/// <summary>
///     This class periodically sweeps <see cref="IRecursiveCache" /> for expired and
///     over-capacity entries, so memory used by long-expired answers is actually freed rather
///     than only ever being noticed -- and skipped past -- the next time that exact
///     (name, type, class) happens to be looked up again.
/// </summary>
/// <param name="cache">The cache this service sweeps.</param>
public sealed class RecursiveCacheEvictionService(IRecursiveCache cache) : BackgroundService
{
    /// <summary>
    ///     This constant contains how often this service sweeps the cache.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
                cache.EvictExpiredAndOverCapacity();
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
        }
    }
}
