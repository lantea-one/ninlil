using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientListAssignmentRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsClientListAssignmentMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DnsClientListAssignmentEntity>(
        context.DnsClientListAssignments,
        changeTracker
    ),
        IDnsClientListAssignmentRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientListAssignmentEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(assignment => assignment.ClientId == clientId)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientListAssignmentEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(assignment => assignment.ClientId == clientId)
            .SortBy(assignment => assignment.CreatedAtUtc)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                assignment => assignment.ClientId == clientId,
                cancellationToken: stoppingToken
            );
}
