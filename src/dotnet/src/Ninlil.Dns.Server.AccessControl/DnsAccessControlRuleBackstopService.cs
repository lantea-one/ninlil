using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl;

/// <summary>
///     This hosted service is the time-to-live backstop of <see cref="DnsAccessControlRuleCache" />.
///     The cache reloads on startup and on a <c>DnsAccessControlRuleChangedEvent</c> of this
///     instance, and an event made on another instance that shares the database never reaches it.
///     Every <see cref="ReloadInterval" /> this service reloads the whole rule set, which is small
///     and flat, so no change detection is needed.
/// </summary>
/// <param name="ruleCache">The cache to reload.</param>
/// <param name="timeProvider">The clock of the reload timer, so tests can drive it.</param>
/// <param name="logger">The logger for reload failures.</param>
public sealed class DnsAccessControlRuleBackstopService(
    DnsAccessControlRuleCache ruleCache,
    TimeProvider timeProvider,
    ILogger<DnsAccessControlRuleBackstopService> logger
) : BackgroundService
{
    /// <summary>
    ///     This field is the time between two reloads. It matches the age limit of the other
    ///     time-to-live caches (client directory, filter configuration, rewrites, schedules and
    ///     forwarder zones).
    /// </summary>
    public static readonly TimeSpan ReloadInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     This property counts the reloads that have finished, whether or not they succeeded.
    ///     Tests wait on it.
    /// </summary>
    public long CompletedReloadCount => Interlocked.Read(ref _completedReloadCount);

    private long _completedReloadCount;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Create the timer before the first await, so it exists once the task has started.
        using PeriodicTimer timer = new(ReloadInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ruleCache.ReloadAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A database outage must not end the loop. The cache keeps the last good
                    // snapshot and the next tick tries again.
                    logger.LogError(exception, "The access-control rule backstop reload failed.");
                }
                finally
                {
                    Interlocked.Increment(ref _completedReloadCount);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }
}
