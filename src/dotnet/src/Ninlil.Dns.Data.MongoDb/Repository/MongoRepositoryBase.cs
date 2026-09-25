using System.Linq.Expressions;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IRepository{TEntity}" /> once, generically, against one
///     MongoDB collection. Every Mongo repository inherits from it instead of repeating the CRUD
///     operations. Writes are staged on <see cref="MongoChangeTracker" /> and run when the unit of
///     work saves.
/// </summary>
/// <param name="collection">The collection this repository stores documents in.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
/// <typeparam name="TEntity">The type of entity this repository stores.</typeparam>
public abstract class MongoRepositoryBase<TEntity>(
    IMongoCollection<TEntity> collection,
    MongoChangeTracker changeTracker
) : IRepository<TEntity>
    where TEntity : EntityBase
{
    /// <summary>
    ///     This property contains the collection the repository operates against. It is
    ///     <see langword="protected" /> so a derived repository can build its own queries on it.
    /// </summary>
    protected IMongoCollection<TEntity> Collection { get; } = collection;

    /// <summary>
    ///     This property contains the change tracker that <see cref="AddAsync" />,
    ///     <see cref="Update" />, and <see cref="Remove" /> stage their writes onto, so that
    ///     <see cref="IUnitOfWork.SaveChangesAsync" /> can perform them all together.
    /// </summary>
    protected MongoChangeTracker ChangeTracker { get; } = changeTracker;

    /// <inheritdoc />
    public virtual Task<TEntity> GetByIdAsync(Guid id, CancellationToken stoppingToken = default) =>
        Collection.Find(entity => entity.Id == id).FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) => await Collection.Find(predicate ?? (_ => true)).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public virtual Task AddAsync(TEntity entity, CancellationToken stoppingToken = default)
    {
        // Staged, so it is written only when IUnitOfWork.SaveChangesAsync runs.
        ChangeTracker.Stage(token => Collection.InsertOneAsync(entity, cancellationToken: token));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual void Update(TEntity entity) =>
        ChangeTracker.Stage(token =>
            Collection.ReplaceOneAsync(
                existing => existing.Id == entity.Id,
                entity,
                cancellationToken: token
            )
        );

    /// <inheritdoc />
    public virtual void Remove(TEntity entity) =>
        ChangeTracker.Stage(token =>
            Collection.DeleteOneAsync(existing => existing.Id == entity.Id, token)
        );
}
