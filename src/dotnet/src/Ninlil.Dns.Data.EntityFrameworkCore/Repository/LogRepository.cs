using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="ILogRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class LogRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<LogEntity>(context),
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
    )
    {
        // Start narrowing from the time range and minimum severity, which every search applies.
        IQueryable<LogEntity> query = Set.Where(log =>
            log.TimestampUtc >= fromUtc
            && log.TimestampUtc <= toUtc
            && log.Severity >= minimumSeverity
        );

        // Apply the category filter, if one was given.
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(log => log.Category == category);

        // We're done, page the newest matches first and materialize them.
        return await query
            .OrderByDescending(log => log.TimestampUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);
    }
}
