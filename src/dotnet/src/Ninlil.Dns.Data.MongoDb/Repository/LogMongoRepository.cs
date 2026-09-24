using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="ILogRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class LogMongoRepository(NinlilDnsMongoContext context, MongoChangeTracker changeTracker)
    : MongoRepositoryBase<LogEntity>(context.Logs, changeTracker),
        ILogRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LogEntity>> SearchAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        LogSeverityEnum minimumSeverity,
        string category,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(log =>
                log.TimestampUtc >= fromUtc
                && log.TimestampUtc <= toUtc
                && log.Severity >= minimumSeverity
                && (string.IsNullOrEmpty(category) || log.Category == category)
            )
            .SortByDescending(log => log.TimestampUtc)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);
}
