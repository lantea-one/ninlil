using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog;

/// <summary>
///     This class prunes the query log every <see cref="QueryLoggingOptions.CleanupInterval" />
///     so it cannot grow without bound, which would otherwise be a denial-of-service risk since
///     logging is on by default. Each sweep applies two bounds in order: entries older than
///     <see cref="QueryLoggingOptions.RetentionPeriod" /> are removed, then, if more than
///     <see cref="QueryLoggingOptions.MaxRetainedEntries" /> remain, the oldest surplus is
///     removed. The sweep runs even when <see cref="QueryLoggingOptions.Enable" /> is off, so
///     entries written before logging was disabled still age out.
/// </summary>
/// <param name="scopeFactory">The factory used to open a scope for the repository per sweep.</param>
/// <param name="options">The retention bounds and sweep interval.</param>
/// <param name="logger">The logger this service records the outcome of each sweep to.</param>
public sealed class DnsQueryLogCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<QueryLoggingOptions> options,
    ILogger<DnsQueryLogCleanupService> logger
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.Value.CleanupInterval, stoppingToken);
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
        }
    }

    /// <summary>
    ///     This method runs one sweep: the age bound first, then the row-count bound. It is public
    ///     so tests can run a sweep without waiting for
    ///     <see cref="QueryLoggingOptions.CleanupInterval" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>How many entries were removed by the age bound and by the row-count bound.</returns>
    public async Task<(int DeletedByAge, int DeletedByCount)> SweepAsync(
        CancellationToken stoppingToken = default
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        int deletedByAge = 0;

        if (options.Value.RetentionPeriod > TimeSpan.Zero)
        {
            DateTimeOffset cutoffUtc = DateTimeOffset.UtcNow - options.Value.RetentionPeriod;
            deletedByAge = await repository.DeleteOlderThanAsync(cutoffUtc, stoppingToken);
        }

        int deletedByCount = 0;

        if (options.Value.MaxRetainedEntries > 0)
            deletedByCount = await repository.DeleteExcessAsync(
                options.Value.MaxRetainedEntries,
                stoppingToken
            );

        if (deletedByAge > 0 || deletedByCount > 0)
        {
            // Large deletes change the data the SQLite planner's statistics describe.
            await repository.OptimizeAsync(stoppingToken);

            logger.LogInformation(
                "Query log cleanup removed {DeletedByAge} entries past the retention window and {DeletedByCount} entries beyond the maximum retained count.",
                deletedByAge,
                deletedByCount
            );
        }

        return (deletedByAge, deletedByCount);
    }
}
