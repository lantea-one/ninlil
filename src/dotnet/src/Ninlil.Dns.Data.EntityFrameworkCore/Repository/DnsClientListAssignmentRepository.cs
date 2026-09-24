using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientListAssignmentRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsClientListAssignmentRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsClientListAssignmentEntity>(context),
        IDnsClientListAssignmentRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientListAssignmentEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) => await Set.Where(assignment => assignment.ClientId == clientId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientListAssignmentEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(assignment => assignment.ClientId == clientId)
            .OrderBy(assignment => assignment.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(assignment => assignment.ClientId == clientId, stoppingToken);
}
