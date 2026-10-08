using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog;

/// <summary>
///     This class is the single consumer of <see cref="DnsQueryLogQueue" />. It collects entries
///     into batches and saves each batch with one scope, one add per entry and one
///     <c>SaveChangesAsync</c>, so a busy resolver costs a few database round trips per second
///     instead of one per query. <see cref="DnsQueryLogSubscriber" /> only fills the queue, so
///     a slow database never holds up DNS answers or other event subscribers.
///     <para>
///         A batch ends at <see cref="BatchSize" /> entries or <see cref="FlushInterval" /> after
///         its first entry, whichever comes first. A failed save is logged as an error and loses
///         that batch; the next batch starts clean. On shutdown the entries still queued are
///         saved before the service stops.
///     </para>
///     <para>
///         When <see cref="DnsQueryLogQueue" /> drops entries because it is full, this service logs
///         one warning with the number dropped since the last warning, at most once per
///         <see cref="DropReportInterval" />.
///     </para>
/// </summary>
/// <param name="queue">The queue to drain.</param>
/// <param name="scopeFactory">The factory used to open a scope for the repository per batch.</param>
/// <param name="logger">The logger used to report failed writes and dropped entries.</param>
public sealed class DnsQueryLogWriterService(
    DnsQueryLogQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DnsQueryLogWriterService> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant is the largest number of entries saved in one batch.
    /// </summary>
    public const int BatchSize = 500;

    private long _reportedDropped;
    private DateTime _lastDropReportUtc = DateTime.UtcNow;

    /// <summary>
    ///     This property contains how long a batch waits for more entries after its first one.
    ///     Tests shorten it.
    /// </summary>
    internal TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     This property contains the minimum time between two warnings about dropped entries.
    ///     Tests shorten it.
    /// </summary>
    internal TimeSpan DropReportInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                List<DnsQueryLogEntryEntity> batch = await CollectBatchAsync(stoppingToken);
                await WriteBatchAsync(batch, CancellationToken.None);
                ReportDropped(force: false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during a graceful shutdown; the remainder is drained below.
        }

        await DrainAsync();
        ReportDropped(force: true);
    }

    /// <summary>
    ///     This method takes the first available entry, then keeps adding until the batch is full
    ///     or <see cref="FlushInterval" /> has passed.
    /// </summary>
    private async Task<List<DnsQueryLogEntryEntity>> CollectBatchAsync(
        CancellationToken stoppingToken
    )
    {
        List<DnsQueryLogEntryEntity> batch = [];
        using CancellationTokenSource flushTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        flushTimeout.CancelAfter(FlushInterval);

        try
        {
            while (batch.Count < BatchSize)
            {
                if (queue.Reader.TryRead(out DnsQueryLogEntryEntity entry))
                    batch.Add(entry);
                else if (!await queue.Reader.WaitToReadAsync(flushTimeout.Token))
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The flush interval elapsed, or the service is stopping. Either way the entries
            // already taken must still be written, so the batch is returned.
        }

        return batch;
    }

    /// <summary>
    ///     This method writes what is left in the queue in batches after the service was asked to
    ///     stop.
    /// </summary>
    private async Task DrainAsync()
    {
        List<DnsQueryLogEntryEntity> batch = [];

        while (queue.Reader.TryRead(out DnsQueryLogEntryEntity entry))
        {
            batch.Add(entry);

            if (batch.Count < BatchSize)
                continue;

            await WriteBatchAsync(batch, CancellationToken.None);
            batch = [];
        }

        await WriteBatchAsync(batch, CancellationToken.None);
    }

    /// <summary>
    ///     This method saves <paramref name="batch" /> in one scope with one save. A failure is
    ///     logged and the batch is lost.
    /// </summary>
    private async Task WriteBatchAsync(
        List<DnsQueryLogEntryEntity> batch,
        CancellationToken stoppingToken
    )
    {
        if (batch.Count == 0)
            return;

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IDnsQueryLogRepository repository =
                scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach (DnsQueryLogEntryEntity entry in batch)
                await repository.AddAsync(entry, stoppingToken);

            await unitOfWork.SaveChangesAsync(stoppingToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unhandled exception while persisting {Count} query log entries; the batch is lost.",
                batch.Count
            );
        }
    }

    /// <summary>
    ///     This method logs how many entries were dropped since the last report, if any, and if
    ///     <see cref="DropReportInterval" /> has passed or <paramref name="force" /> is set.
    /// </summary>
    private void ReportDropped(bool force)
    {
        DateTime now = DateTime.UtcNow;

        if (!force && now - _lastDropReportUtc < DropReportInterval)
            return;

        long total = queue.DroppedCount;
        long newlyDropped = total - _reportedDropped;

        if (newlyDropped <= 0)
            return;

        _reportedDropped = total;
        _lastDropReportUtc = now;
        logger.LogWarning(
            "The query log queue was full; {Count} entries were dropped since the last report.",
            newlyDropped
        );
    }
}
