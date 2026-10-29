using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting;

/// <summary>
///     This class runs every <see cref="DnsRateLimitOptions.CleanupInterval" /> and evicts the
///     per-IP buckets and subnet windows of <see cref="TokenBucketDnsRateLimiter" /> that have not
///     seen a query for <see cref="DnsRateLimitOptions.TrackedClientExpiry" />, so a client that
///     stops querying does not stay in memory. It does nothing when rate limiting is disabled.
/// </summary>
/// <param name="rateLimiter">The limiter whose tracked buckets this service sweeps.</param>
/// <param name="options">The sweep interval and eviction age.</param>
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
    ///     This method removes every per-IP bucket and subnet window whose client or subnet has
    ///     been silent for longer than <see cref="DnsRateLimitOptions.TrackedClientExpiry" />. Both
    ///     use that one setting; there is no separate expiry for subnets.
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
                rateLimiter.TryRemoveBucket(address);
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
                rateLimiter.TryRemoveSubnetWindow(subnetKey);
        }
    }
}
