using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientGroupListAssignmentRepository" /> against
///     MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsClientGroupListAssignmentMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DnsClientGroupListAssignmentEntity>(
        context.DnsClientGroupListAssignments,
        changeTracker
    ),
        IDnsClientGroupListAssignmentRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupListAssignmentEntity>> ListByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(assignment => assignment.ClientGroupId == clientGroupId)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupListAssignmentEntity>> SearchByGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(assignment => assignment.ClientGroupId == clientGroupId)
            .SortBy(assignment => assignment.CreatedAtUtc)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                assignment => assignment.ClientGroupId == clientGroupId,
                cancellationToken: stoppingToken
            );
}
