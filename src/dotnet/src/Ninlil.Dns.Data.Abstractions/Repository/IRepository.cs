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
    ///     This method stages a new <typeparamref name="TEntity" /> for insertion. The insert is
    ///     not guaranteed to be visible to other readers until
    ///     <see cref="IUnitOfWork.SaveChangesAsync" /> completes.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task AddAsync(TEntity entity, CancellationToken stoppingToken = default);

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
