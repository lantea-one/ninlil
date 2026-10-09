using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb;

/// <summary>
///     This class implements <see cref="IUnitOfWork" /> by flushing the writes every Mongo
///     repository staged onto the shared <see cref="MongoChangeTracker" />. See
///     <see cref="MongoChangeTracker" /> for why MongoDB needs this indirection when the
///     relational providers do not. The save is all-or-nothing on a MongoDB replica set; on a
///     standalone server, and for filter-list refreshes, it is not.
/// </summary>
/// <param name="changeTracker">The change tracker shared by every repository this request used.</param>
public class MongoUnitOfWork(MongoChangeTracker changeTracker) : IUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default) =>
        changeTracker.FlushAsync(stoppingToken);
}
