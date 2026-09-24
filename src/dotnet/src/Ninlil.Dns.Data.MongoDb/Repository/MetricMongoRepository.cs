using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IMetricRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class MetricMongoRepository(NinlilDnsMongoContext context, MongoChangeTracker changeTracker)
    : MongoRepositoryBase<MetricEntity>(context.Metrics, changeTracker),
        IMetricRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricEntity>> QueryAsync(
        string category,
        string name,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(metric =>
                metric.Category == category
                && metric.Name == name
                && metric.TimestampUtc >= fromUtc
                && metric.TimestampUtc <= toUtc
            )
            .SortBy(metric => metric.TimestampUtc)
            .ToListAsync(stoppingToken);
}
