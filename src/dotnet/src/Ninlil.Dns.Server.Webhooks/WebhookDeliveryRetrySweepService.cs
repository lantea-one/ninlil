using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This class is the sole mechanism that ever performs a webhook delivery's second or later
///     attempt -- the real, on-disk durability this project's outbound webhook feature previously
///     lacked entirely. Every prior version of this feature kept a delivery's whole retry/backoff
///     loop purely in memory, inside one detached background task, for the lifetime of a single
///     process; a restart at any point mid-loop silently lost the delivery with no record it was
///     ever attempted at all. This service closes that gap by making retry scheduling itself a
///     real, persisted fact (<see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" />) rather
///     than an in-process timer: on a fixed interval (<see cref="WebhookDeliveryOptions.SweepInterval" />),
///     it asks <see cref="IWebhookDeliveryAttemptRepository.ListDueForRetryAsync" /> for every
///     delivery currently due, and resumes each one via <see cref="WebhookDeliveryService.ResumeAttemptAsync" />.
///     <para>
///         <b>Why this alone is enough to prove real restart durability, with no separate
///         "interrupted"/"orphaned" bookkeeping needed at all</b>: a delivery interrupted by a
///         process restart -- whether it was genuinely waiting out its own backoff, or was itself
///         mid-attempt when the process died -- is, by construction, left on disk as
///         <see cref="Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending" /> with a
///         <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> that either already has, or
///         soon will, elapse. The very first sweep a freshly started process runs -- triggered
///         immediately at startup, before this service's own first interval wait -- finds that row
///         exactly the same way any later sweep in the same, uninterrupted process would have, and
///         resumes it through the exact same code path. No process ever needs to know whether it
///         is "resuming after a crash" versus "continuing its own earlier work" -- from this
///         service's own point of view, those are the same operation.
///     </para>
///     <para>
///         This is also what keeps delivery safely at-least-once rather than needing any
///         cross-process locking: only one process is ever running against this node's own local
///         storage at a time (clustering propagates configuration between independently-storaged
///         nodes over its own channel -- see <c>Ninlil.Dns.Server.Cluster</c> -- it does not share
///         one database across nodes), and within one process this service's own sweep loop never
///         overlaps itself (each pass runs to completion, including every delivery it resumes,
///         before the next interval wait begins), so a given delivery is never concurrently
///         resumed by two callers at once.
///     </para>
///     <para>
///         Each sweep also runs a bounded retention cleanup
///         (<see cref="IWebhookDeliveryAttemptRepository.DeleteCompletedOlderThanAsync" />,
///         governed by <see cref="WebhookDeliveryOptions.AttemptRetentionPeriod" />), the same
///         "an operational record must eventually age out on a server left running for years"
///         reasoning <c>Ninlil.Dns.Server.QueryLog.DnsQueryLogCleanupService</c> already
///         establishes for the query log (a plain text reference, not
///         <see langword="see cref" />, since this project has no dependency on
///         <c>Server.QueryLog</c>).
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach fresh, scoped services per sweep.</param>
/// <param name="options">This service's own configured sweep interval and retention period.</param>
/// <param name="logger">The logger this service records the outcome of each sweep to.</param>
public sealed class WebhookDeliveryRetrySweepService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<WebhookDeliveryOptions> options,
    ILogger<WebhookDeliveryRetrySweepService> logger
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The very first sweep runs immediately, not after waiting out one full SweepInterval --
        // this is what actually resumes a delivery left behind by a prior process's own crash or
        // restart as promptly as this service can, rather than leaving it stranded for up to one
        // whole interval after this fresh process has already come up.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
                await Task.Delay(options.CurrentValue.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
            catch (Exception exception)
            {
                // A single failed sweep pass (a transient storage error -- most plausibly a
                // SQLite write-lock conflict from another concurrent writer against the same
                // database file, but any other transient fault applies equally) must never take
                // this whole background service down, the same "one bad check doesn't kill the
                // loop" reasoning TrustAnchorRolloverService's own check loop already establishes
                // for the exact same shape of periodic background work. There is always another
                // sweep, at worst SweepInterval later, and a delivery genuinely due for a retry
                // simply waits one more interval rather than being lost.
                logger.LogWarning(exception, "A webhook delivery retry sweep failed unexpectedly.");
            }
        }
    }

    /// <summary>
    ///     This method performs a single sweep pass against fresh, scoped services: resuming
    ///     every delivery currently due for a retry, then applying the retention cleanup bound --
    ///     exposed publicly so a caller (a test, or a future operator-triggered manual sweep) can
    ///     drive and observe one pass directly rather than waiting out a real
    ///     <see cref="WebhookDeliveryOptions.SweepInterval" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>How many deliveries were resumed, and how many completed rows the retention bound removed.</returns>
    public async Task<(int Resumed, int Retained)> SweepAsync(
        CancellationToken stoppingToken = default
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        IWebhookDeliveryAttemptRepository attemptRepository =
            scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
        WebhookDeliveryService deliveryService =
            scope.ServiceProvider.GetRequiredService<WebhookDeliveryService>();

        IReadOnlyList<WebhookDeliveryAttemptEntity> due =
            await attemptRepository.ListDueForRetryAsync(DateTimeOffset.UtcNow, stoppingToken);

        if (due.Count > 0)
        {
            logger.LogDebug(
                "Webhook delivery retry sweep resuming {Count} due deliveries.",
                due.Count
            );

            await Task.WhenAll(
                due.Select(attempt => deliveryService.ResumeAttemptAsync(attempt, stoppingToken))
            );
        }

        int removed = 0;

        if (options.CurrentValue.AttemptRetentionPeriod > TimeSpan.Zero)
        {
            DateTimeOffset cutoffUtc =
                DateTimeOffset.UtcNow - options.CurrentValue.AttemptRetentionPeriod;
            removed = await attemptRepository.DeleteCompletedOlderThanAsync(
                cutoffUtc,
                stoppingToken
            );

            if (removed > 0)
                logger.LogInformation(
                    "Webhook delivery retention cleanup removed {Count} completed delivery records past the retention window.",
                    removed
                );
        }

        return (due.Count, removed);
    }
}
