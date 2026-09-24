using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog;

/// <summary>
///     This class periodically prunes the persistent query log so that it can never grow
///     unboundedly -- the exact denial-of-service concern this project's own "capability-
///     expanding toggles default off" invariant normally guards against, still required here even
///     though query logging itself is on by default. Two independent bounds are enforced on every
///     sweep, in order: every entry older than <see cref="QueryLoggingOptions.RetentionPeriod" />
///     is removed first, then, if the log still holds more than
///     <see cref="QueryLoggingOptions.MaxRetainedEntries" /> entries, the oldest surplus entries
///     are removed too. This sweep runs regardless of <see cref="QueryLoggingOptions.Enable" />,
///     since disabling query logging stops new writes but says nothing about entries already on
///     disk from before it was disabled -- those still need to age out on schedule rather than
///     sitting there forever.
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped repository per sweep.</param>
/// <param name="options">This service's own configured retention bounds and sweep interval.</param>
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
    ///     This method performs a single retention sweep against a fresh, scoped repository,
    ///     applying the age bound first and the row-count bound second, and returns how many
    ///     entries each bound actually removed -- exposed publicly so a caller (a test, or an
    ///     operator-triggered manual sweep) can drive and observe one pass directly rather than
    ///     waiting out a real <see cref="QueryLoggingOptions.CleanupInterval" />.
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
            logger.LogInformation(
                "Query log cleanup removed {DeletedByAge} entries past the retention window and {DeletedByCount} entries beyond the maximum retained count.",
                deletedByAge,
                deletedByCount
            );

        return (deletedByAge, deletedByCount);
    }
}
