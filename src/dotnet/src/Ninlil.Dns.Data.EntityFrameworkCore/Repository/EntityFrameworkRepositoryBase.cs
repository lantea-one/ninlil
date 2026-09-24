using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IRepository{TEntity}" /> once, generically, against
///     <see cref="NinlilDnsContext" />. Every concrete Entity Framework Core repository in
///     this project inherits from this class rather than re-implementing the same handful of
///     CRUD operations against its own <see cref="DbSet{TEntity}" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
/// <typeparam name="TEntity">The type of entity this repository stores.</typeparam>
public abstract class EntityFrameworkRepositoryBase<TEntity>(NinlilDnsContext context)
    : IRepository<TEntity>
    where TEntity : EntityBase
{
    /// <summary>
    ///     This property contains the database context every method below operates against. It
    ///     is exposed as <see langword="protected" /> rather than kept private so that a derived
    ///     repository can reach the context's other <see cref="DbSet{TEntity}" /> properties when
    ///     it needs a query that spans more than one entity type.
    /// </summary>
    protected NinlilDnsContext Context { get; } = context;

    /// <summary>
    ///     This property contains the <see cref="DbSet{TEntity}" /> this repository operates
    ///     against.
    /// </summary>
    protected DbSet<TEntity> Set { get; } = context.Set<TEntity>();

    /// <inheritdoc />
    public virtual Task<TEntity> GetByIdAsync(Guid id, CancellationToken stoppingToken = default) =>
        Set.FirstOrDefaultAsync(entity => entity.Id == id, stoppingToken);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    )
    {
        // Start from the full set, then narrow it down if a predicate was provided.
        IQueryable<TEntity> query = Set;

        // Apply the predicate, if one was given.
        if (predicate is not null)
            query = query.Where(predicate);

        // We're done, materialize and return the matching entities.
        return await query.ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public virtual async Task AddAsync(TEntity entity, CancellationToken stoppingToken = default) =>
        await Set.AddAsync(entity, stoppingToken);

    /// <inheritdoc />
    public virtual void Update(TEntity entity) => Set.Update(entity);

    /// <inheritdoc />
    public virtual void Remove(TEntity entity) => Set.Remove(entity);
}
