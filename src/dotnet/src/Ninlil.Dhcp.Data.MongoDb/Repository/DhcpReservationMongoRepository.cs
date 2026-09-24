using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDhcpReservationRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DhcpReservationMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DhcpReservationEntity>(context.DhcpReservations, changeTracker),
        IDhcpReservationRepository
{
    /// <inheritdoc />
    public Task<DhcpReservationEntity> FindByClientIdentifierAsync(
        Guid scopeId,
        string clientIdentifier,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(reservation =>
                reservation.ScopeId == scopeId && reservation.ClientIdentifier == clientIdentifier
            )
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public Task<DhcpReservationEntity> FindByIpAddressAsync(
        Guid scopeId,
        string ipAddress,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(reservation =>
                reservation.ScopeId == scopeId && reservation.IpAddress == ipAddress
            )
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpReservationEntity>> SearchByScopeIdAsync(
        Guid scopeId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(reservation => reservation.ScopeId == scopeId)
            .SortBy(reservation => reservation.ClientIdentifier)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByScopeIdAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                reservation => reservation.ScopeId == scopeId,
                cancellationToken: stoppingToken
            );
}
