using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface describes the read and write operations every persisted entity supports,
///     whichever storage engine implements it. Entity-specific repositories, such as
///     <see cref="IZoneRepository" />, add lookups that only make sense for their entity. Writes
///     are staged and saved together by <see cref="IUnitOfWork" />.
///     <para>
///         Repositories behind a paged REST list add a <c>SearchAsync</c> and a
///         <c>CountAsync</c>. These page in the database (<c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>
///         or the MongoDB equivalent) instead of sorting a full <see cref="ListAsync" /> result in
///         memory. They take a criteria object only when the list endpoint has filters (see
///         <see cref="IDnsQueryLogRepository" />); otherwise they take just the page bounds.
///     </para>
/// </summary>
/// <typeparam name="TEntity">The type of entity this repository stores.</typeparam>
public interface IRepository<TEntity>
    where TEntity : EntityBase
{
    /// <summary>
    ///     This method looks up a single <typeparamref name="TEntity" /> by its identifier.
    /// </summary>
    /// <param name="id">The identifier to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The matching <typeparamref name="TEntity" />, or <see langword="null" /> if no entity
    ///     with that identifier exists.
    /// </returns>
    Task<TEntity> GetByIdAsync(Guid id, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists every <typeparamref name="TEntity" /> that matches an optional
    ///     <paramref name="predicate" />.
    /// </summary>
    /// <param name="predicate">
    ///     An optional filter; when <see langword="null" />, every entity is returned.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching <typeparamref name="TEntity" /> instances.</returns>
    Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists the identifiers, and nothing else, of every
    ///     <typeparamref name="TEntity" /> that matches an optional <paramref name="predicate" />.
    ///     It is a snapshot of ids taken in one query. A batch job that then loads each row with
    ///     <see cref="GetByIdAsync" /> sees a row deleted mid-pass as missing and skips it, whereas
    ///     paging with an offset would shift every later row by one and skip a live row. Ids are
    ///     16 bytes each and are held in memory, so use it for internal batches, not for a REST
    ///     list (those use <c>SearchAsync</c>/<c>CountAsync</c>). The default implementation lists
    ///     the entities and keeps the ids; the Entity Framework Core and MongoDB base classes
    ///     override it with a projection that never loads the entities.
    /// </summary>
    /// <param name="predicate">
    ///     An optional filter; when <see langword="null" />, every identifier is returned.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The identifiers of the matching entities, in no particular order.</returns>
    async Task<IReadOnlyList<Guid>> ListIdsAsync(
        Expression<Func<TEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) => [.. (await ListAsync(predicate, stoppingToken)).Select(entity => entity.Id)];

    /// <summary>
    ///     This method stages a new <typeparamref name="TEntity" /> for insertion. The insert is
    ///     not guaranteed to be visible to other readers until
    ///     <see cref="IUnitOfWork.SaveChangesAsync" /> completes.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task AddAsync(TEntity entity, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method stages many new <typeparamref name="TEntity" /> instances for insertion as
    ///     one bulk operation. It is cheaper than calling <see cref="AddAsync" /> once per entity:
    ///     the MongoDB implementation stages a single ordered <c>InsertMany</c>. As with
    ///     <see cref="AddAsync" />, nothing is guaranteed to be visible to other readers until
    ///     <see cref="IUnitOfWork.SaveChangesAsync" /> completes. The default implementation calls
    ///     <see cref="AddAsync" /> per entity; the Entity Framework Core and MongoDB base classes
    ///     override it.
    /// </summary>
    /// <param name="entities">The entities to add; an empty collection stages nothing.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    async Task AddRangeAsync(
        IReadOnlyCollection<TEntity> entities,
        CancellationToken stoppingToken = default
    )
    {
        foreach (TEntity entity in entities)
            await AddAsync(entity, stoppingToken);
    }

    /// <summary>
    ///     This method stages an update to an existing <typeparamref name="TEntity" />. The
    ///     update is not guaranteed to be visible to other readers until
    ///     <see cref="IUnitOfWork.SaveChangesAsync" /> completes.
    /// </summary>
    /// <param name="entity">The entity to update, with its new values already set.</param>
    void Update(TEntity entity);

    /// <summary>
    ///     This method stages the removal of an existing <typeparamref name="TEntity" />. The
    ///     removal is not guaranteed to take effect until
    ///     <see cref="IUnitOfWork.SaveChangesAsync" /> completes.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    void Remove(TEntity entity);
}
