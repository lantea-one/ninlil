using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Catalog;

/// <summary>
///     This record describes how to export and import every row of one entity type through its
///     own already-existing repository, without this project ever needing a second, parallel copy
///     of any entity's shape. Every function below is built inside <see cref="For{TEntity, TRepository}" />,
///     where <c>TEntity</c> is still a concrete type the compiler knows, so the
///     actual JSON serialization always happens against that concrete type rather than the boxed
///     <see cref="EntityBase" /> this record exposes it as -- passing an interface- or base-typed
///     value into <c>Ninlil.Common.Serializer.JsonSerializer</c>'s own single-argument
///     overload silently serializes based on that base type's own (empty) shape instead of the
///     real one, a mistake this project has been bitten by before.
/// </summary>
/// <param name="Name">
///     This entity type's own name inside the archive -- both the JSON file name under
///     <c>database/</c> and the key <see cref="Model.BackupManifest.EntityCounts" /> reports it
///     under.
/// </param>
/// <param name="ExportJsonAsync">Exports every row of this entity type as a JSON array.</param>
/// <param name="ImportJsonAsync">Imports a JSON array of this entity type, returning how many rows it added.</param>
/// <param name="WipeAsync">Removes every existing row of this entity type, returning how many it removed.</param>
public sealed record BackupEntityDescriptor(
    string Name,
    Func<IServiceProvider, CancellationToken, Task<string>> ExportJsonAsync,
    Func<IServiceProvider, string, CancellationToken, Task<int>> ImportJsonAsync,
    Func<IServiceProvider, CancellationToken, Task<int>> WipeAsync
)
{
    /// <summary>
    ///     This method builds a <see cref="BackupEntityDescriptor" /> for one entity type, given
    ///     the repository interface that already implements <see cref="IRepository{TEntity}" />
    ///     for it.
    /// </summary>
    /// <typeparam name="TEntity">The concrete entity type this descriptor exports and imports.</typeparam>
    /// <typeparam name="TRepository">The repository interface that reads and writes it.</typeparam>
    /// <param name="name">This entity type's own name inside the archive.</param>
    public static BackupEntityDescriptor For<TEntity, TRepository>(string name)
        where TEntity : EntityBase
        where TRepository : IRepository<TEntity>
    {
        return new BackupEntityDescriptor(
            name,
            async (services, stoppingToken) =>
            {
                TRepository repository = services.GetRequiredService<TRepository>();
                IReadOnlyList<TEntity> entities = await repository.ListAsync(
                    stoppingToken: stoppingToken
                );

                // Serialize while TEntity is still a concrete, compile-time-known type, so this
                // always reflects the real entity shape rather than a base type's own.
                return Ninlil.Common.Serializer.JsonSerializer.Serialize(entities.ToList());
            },
            async (services, json, stoppingToken) =>
            {
                TRepository repository = services.GetRequiredService<TRepository>();
                List<TEntity> entities =
                    Ninlil.Common.Serializer.JsonSerializer.Deserialize<List<TEntity>>(json) ?? [];

                foreach (TEntity entity in entities)
                    await repository.AddAsync(entity, stoppingToken);

                return entities.Count;
            },
            async (services, stoppingToken) =>
            {
                TRepository repository = services.GetRequiredService<TRepository>();
                IReadOnlyList<TEntity> existing = await repository.ListAsync(
                    stoppingToken: stoppingToken
                );

                foreach (TEntity entity in existing)
                    repository.Remove(entity);

                return existing.Count;
            }
        );
    }
}
