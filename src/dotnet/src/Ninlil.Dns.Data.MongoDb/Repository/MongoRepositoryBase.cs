using System.Linq.Expressions;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IRepository{TEntity}" /> once, generically, against a
///     single MongoDB collection. Every concrete Mongo repository in this project inherits from
///     this class rather than re-implementing the same handful of CRUD operations against its
///     own <see cref="IMongoCollection{TEntity}" />.
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
    ///     This property contains the collection every method below operates against. It is
    ///     exposed as <see langword="protected" /> rather than kept private so that a derived
    ///     repository can build additional MongoDB-specific queries against it directly.
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
        // Stage the insert rather than performing it immediately, so it is only written once
        // IUnitOfWork.SaveChangesAsync is called.
        ChangeTracker.Stage(token => Collection.InsertOneAsync(entity, cancellationToken: token));

        // We're done; there is nothing to await yet, since the insert has only been staged.
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
