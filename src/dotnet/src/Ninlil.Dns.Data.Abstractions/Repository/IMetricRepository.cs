using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the time-range query the
///     dashboard's charts need in order to plot a metric over time.
/// </summary>
public interface IMetricRepository : IRepository<MetricEntity>
{
    /// <summary>
    ///     This method lists every sample of a named metric within a time range.
    /// </summary>
    /// <param name="category">The metric's category, such as <c>"dns"</c>.</param>
    /// <param name="name">The metric's name, such as <c>"queries.answered.cache"</c>.</param>
    /// <param name="fromUtc">The inclusive start of the time range to search.</param>
    /// <param name="toUtc">The inclusive end of the time range to search.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching samples, ordered from oldest to newest.</returns>
    Task<IReadOnlyList<MetricEntity>> QueryAsync(
        string category,
        string name,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken stoppingToken = default
    );
}
