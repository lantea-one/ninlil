using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IRepository{TEntity}" /> once, generically, against
///     <see cref="NinlilDnsContext" />. Every Entity Framework Core repository in this project
///     inherits from it instead of repeating the same CRUD operations. Writes are only staged;
///     <see cref="EntityFrameworkUnitOfWork" /> saves them.
/// </summary>
/// <param name="context">The database context to operate against.</param>
/// <typeparam name="TEntity">The type of entity this repository stores.</typeparam>
public abstract class EntityFrameworkRepositoryBase<TEntity>(NinlilDnsContext context)
    : IRepository<TEntity>
    where TEntity : EntityBase
{
    /// <summary>
    ///     This property contains the database context the repository operates against. It is
    ///     <see langword="protected" /> so a derived repository can reach other
    ///     <see cref="DbSet{TEntity}" /> properties for queries across entity types.
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
        IQueryable<TEntity> query = Set;

        if (predicate is not null)
            query = query.Where(predicate);

        return await query.ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public virtual async Task AddAsync(TEntity entity, CancellationToken stoppingToken = default) =>
        await Set.AddAsync(entity, stoppingToken);

    /// <inheritdoc />
    public virtual async Task AddRangeAsync(
        IReadOnlyCollection<TEntity> entities,
        CancellationToken stoppingToken = default
    ) => await Set.AddRangeAsync(entities, stoppingToken);

    /// <inheritdoc />
    public virtual void Update(TEntity entity) => Set.Update(entity);

    /// <inheritdoc />
    public virtual void Remove(TEntity entity) => Set.Remove(entity);
}
