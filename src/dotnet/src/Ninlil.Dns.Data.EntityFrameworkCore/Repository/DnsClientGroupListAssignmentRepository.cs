using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientGroupListAssignmentRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsClientGroupListAssignmentRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsClientGroupListAssignmentEntity>(context),
        IDnsClientGroupListAssignmentRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupListAssignmentEntity>> ListByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(assignment => assignment.ClientGroupId == clientGroupId)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupListAssignmentEntity>> SearchByGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(assignment => assignment.ClientGroupId == clientGroupId)
            .OrderBy(assignment => assignment.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        await Set.CountAsync(
            assignment => assignment.ClientGroupId == clientGroupId,
            stoppingToken
        );
}
