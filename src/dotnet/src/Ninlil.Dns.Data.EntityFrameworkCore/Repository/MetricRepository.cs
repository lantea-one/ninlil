using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IMetricRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class MetricRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<MetricEntity>(context),
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
        await Set.Where(metric =>
                metric.Category == category
                && metric.Name == name
                && metric.TimestampUtc >= fromUtc
                && metric.TimestampUtc <= toUtc
            )
            .OrderBy(metric => metric.TimestampUtc)
            .ToListAsync(stoppingToken);
}
