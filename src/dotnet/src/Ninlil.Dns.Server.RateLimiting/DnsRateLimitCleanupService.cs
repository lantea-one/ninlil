using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting;

/// <summary>
///     This class periodically evicts every tracked bucket <see cref="TokenBucketDnsRateLimiter" />
///     has not seen a query from in at least <see cref="DnsRateLimitOptions.TrackedClientExpiry" />,
///     so a client that stops querying does not sit in memory forever.
/// </summary>
/// <param name="rateLimiter">The limiter whose own tracked buckets this service sweeps.</param>
/// <param name="options">This service's own configured sweep interval and eviction age.</param>
public sealed class DnsRateLimitCleanupService(
    TokenBucketDnsRateLimiter rateLimiter,
    IOptions<DnsRateLimitOptions> options
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enable)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.Value.CleanupInterval, stoppingToken);
                Sweep();
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
        }
    }

    /// <summary>
    ///     This method removes every per-IP bucket and every subnet window whose own client (or
    ///     subnet) has been silent for longer than <see cref="DnsRateLimitOptions.TrackedClientExpiry" />
    ///     -- the same staleness threshold governs both, since neither has its own dedicated
    ///     configuration knob and the two dictionaries otherwise share nothing.
    /// </summary>
    public void Sweep()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - options.Value.TrackedClientExpiry;

        foreach (
            (
                System.Net.IPAddress address,
                TokenBucketDnsRateLimiter.Bucket bucket
            ) in rateLimiter.Buckets
        )
        {
            bool isStale;

            lock (bucket)
                isStale = bucket.LastTouchedUtc < cutoff;

            if (isStale)
                rateLimiter.Buckets.TryRemove(address, out _);
        }

        foreach (
            (
                System.Net.IPAddress subnetKey,
                TokenBucketDnsRateLimiter.SubnetWindow window
            ) in rateLimiter.SubnetWindows
        )
        {
            bool isStale;

            lock (window)
                isStale = window.LastTouchedUtc < cutoff;

            if (isStale)
                rateLimiter.SubnetWindows.TryRemove(subnetKey, out _);
        }
    }
}
