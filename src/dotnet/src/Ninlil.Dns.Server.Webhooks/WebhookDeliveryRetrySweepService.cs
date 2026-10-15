using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This hosted service makes every second and later attempt of a webhook delivery. Retry
///     times are saved in <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> instead of
///     being kept in an in-memory timer. Every <see cref="WebhookDeliveryOptions.SweepInterval" />
///     it asks <see cref="IWebhookDeliveryAttemptRepository.ListDueForRetryAsync" /> for the
///     deliveries that are due and resumes them through
///     <see cref="WebhookDeliveryService.ResumeDueAsync" />.
///     <para>
///         <b>Restarts.</b> A delivery interrupted by a restart, while waiting out its backoff or
///         during a retry, stays saved as
///         <see cref="Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending" /> with a
///         <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> that has passed or soon will.
///         The first sweep runs at startup and resumes it by the same path as any other due
///         delivery, so no separate crash-recovery bookkeeping is needed. A retry interrupted
///         mid-request may be sent again, so delivery is at least once.
///     </para>
///     <para>
///         <b>No overlap.</b> Each sweep finishes, including every delivery it resumes, before
///         the next wait starts, so one process never resumes the same delivery twice at once.
///         Within a sweep the HTTP attempts run in parallel and the database work in sequence
///         (see <see cref="WebhookDeliveryService" />). A sweep that fails waits the interval
///         before the next one.
///         There is no cross-process locking: this assumes one Ninlil process per database.
///         Cluster nodes each have their own storage (see <c>Ninlil.Dns.Server.Cluster</c>).
///     </para>
///     <para>
///         Each sweep also removes completed deliveries older than
///         <see cref="WebhookDeliveryOptions.AttemptRetentionPeriod" />
///         (<see cref="IWebhookDeliveryAttemptRepository.DeleteCompletedOlderThanAsync" />), as
///         <c>DnsQueryLogCleanupService</c> does for the query log.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach fresh, scoped services per sweep.</param>
/// <param name="options">The sweep interval and retention period, read on every sweep.</param>
/// <param name="logger">The logger this service records the outcome of each sweep to.</param>
public sealed class WebhookDeliveryRetrySweepService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<WebhookDeliveryOptions> options,
    ILogger<WebhookDeliveryRetrySweepService> logger
) : BackgroundService
{
    /// <summary>
    ///     This field contains the wait between sweeps used when
    ///     <see cref="WebhookDeliveryOptions.SweepInterval" /> is zero or negative, which would
    ///     otherwise make the loop spin. It matches the option's default.
    /// </summary>
    private static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan sweepInterval = options.CurrentValue.SweepInterval;

        if (sweepInterval <= TimeSpan.Zero)
        {
            logger.LogWarning(
                "The webhook delivery SweepInterval {SweepInterval} is not positive; using {DefaultInterval} instead.",
                sweepInterval,
                DefaultSweepInterval
            );
        }

        // The first sweep runs at once, not after one SweepInterval, so deliveries left by a
        // previous process are resumed promptly.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during a graceful shutdown.
                break;
            }
            catch (Exception exception)
            {
                // A failed sweep (for example a SQLite write-lock conflict) must not stop the
                // service, and must not be retried at once: the wait below runs after a
                // failure too, so a persistent failure does not spin. A due delivery stays
                // saved, so nothing is lost.
                logger.LogWarning(exception, "A webhook delivery retry sweep failed unexpectedly.");
            }

            try
            {
                TimeSpan interval = options.CurrentValue.SweepInterval;
                await Task.Delay(
                    interval > TimeSpan.Zero ? interval : DefaultSweepInterval,
                    stoppingToken
                );
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
        }
    }

    /// <summary>
    ///     This method runs one sweep in a new scope: it resumes every delivery due for a retry
    ///     (the HTTP attempts run in parallel), then removes completed deliveries past the
    ///     retention period. It is public so tests can run one sweep without waiting for
    ///     <see cref="WebhookDeliveryOptions.SweepInterval" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>How many deliveries were resumed, and how many completed rows were removed.</returns>
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

            await deliveryService.ResumeDueAsync(due, stoppingToken);
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
