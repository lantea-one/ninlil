using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Catalog;

/// <summary>
///     This record describes how to export, import and wipe every row of one entity type through
///     its existing repository, so no second copy of the entity shapes is needed. The functions
///     are built in <see cref="For{TEntity, TRepository}" />, where <c>TEntity</c> is the concrete
///     type, so JSON serialization uses the full entity shape. Serializing through
///     <see cref="EntityBase" /> or an interface with the single-argument
///     <c>Ninlil.Common.Serializer.JsonSerializer</c> overload would write only the base type's
///     properties.
/// </summary>
/// <param name="Name">
///     The entity type's name in the archive: the JSON file name under <c>database/</c> and the
///     key in <see cref="Model.BackupManifest.EntityCounts" />.
/// </param>
/// <param name="ExportJsonAsync">Exports every row of this entity type as a JSON array.</param>
/// <param name="Parse">
///     Reads a JSON array of this entity type without touching the database, so a restore can
///     check every file before it wipes anything. It throws <see cref="System.Text.Json.JsonException" />
///     for malformed JSON or a null element.
/// </param>
/// <param name="ImportAsync">Adds already parsed rows (the caller saves), returning how many.</param>
/// <param name="WipeAsync">Marks every existing row of this entity type for removal (the caller saves), returning how many.</param>
public sealed record BackupEntityDescriptor(
    string Name,
    Func<IServiceProvider, CancellationToken, Task<string>> ExportJsonAsync,
    Func<string, IReadOnlyList<EntityBase>> Parse,
    Func<IServiceProvider, IReadOnlyList<EntityBase>, CancellationToken, Task<int>> ImportAsync,
    Func<IServiceProvider, CancellationToken, Task<int>> WipeAsync
)
{
    /// <summary>
    ///     This method builds a <see cref="BackupEntityDescriptor" /> for one entity type, given
    ///     the repository interface that implements <see cref="IRepository{TEntity}" /> for it.
    /// </summary>
    /// <typeparam name="TEntity">The concrete entity type this descriptor exports and imports.</typeparam>
    /// <typeparam name="TRepository">The repository interface that reads and writes it.</typeparam>
    /// <param name="name">The entity type's name in the archive.</param>
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

                // Serialize as the concrete TEntity so the full entity shape is written.
                return Ninlil.Common.Serializer.JsonSerializer.Serialize(entities.ToList());
            },
            json =>
            {
                List<TEntity> entities =
                    Ninlil.Common.Serializer.JsonSerializer.Deserialize<List<TEntity>>(json) ?? [];

                if (entities.Any(entity => entity is null))
                    throw new JsonException("The array holds a null element.");

                return entities;
            },
            async (services, parsed, stoppingToken) =>
            {
                TRepository repository = services.GetRequiredService<TRepository>();

                foreach (TEntity entity in parsed.Cast<TEntity>())
                    await repository.AddAsync(entity, stoppingToken);

                return parsed.Count;
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
