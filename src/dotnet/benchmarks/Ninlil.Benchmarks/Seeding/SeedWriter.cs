using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Seeding;

/// <summary>
///     This class writes generated entities through the repositories in chunks, one scope and
///     one save per chunk, so the EF change tracker never holds more than one chunk and MongoDB
///     keeps each unit of work small.
/// </summary>
/// <param name="services">The service provider of the database being seeded.</param>
/// <param name="log">Where progress lines go.</param>
public sealed class SeedWriter(IServiceProvider services, TextWriter log)
{
    /// <summary>
    ///     This constant contains the number of entities saved per unit of work.
    /// </summary>
    public const int ChunkSize = 5_000;

    /// <summary>
    ///     This method adds every entity of <paramref name="items" /> through the repository
    ///     <typeparamref name="TRepository" /> and saves after each chunk.
    /// </summary>
    /// <param name="label">What is being written, for the progress line.</param>
    /// <param name="items">The entities, enumerated once.</param>
    public async Task AddAllAsync<TEntity, TRepository>(string label, IEnumerable<TEntity> items)
        where TEntity : EntityBase
        where TRepository : IRepository<TEntity>
    {
        List<TEntity> chunk = new(ChunkSize);
        int written = 0;

        foreach (TEntity item in items)
        {
            chunk.Add(item);

            if (chunk.Count < ChunkSize)
                continue;

            await FlushAsync<TEntity, TRepository>(chunk);
            written += chunk.Count;
            chunk.Clear();
            await log.WriteLineAsync($"  {label}: {written:N0} written");
        }

        if (chunk.Count > 0)
        {
            await FlushAsync<TEntity, TRepository>(chunk);
            written += chunk.Count;
        }

        await log.WriteLineAsync($"  {label}: {written:N0} done");
    }

    /// <summary>
    ///     This method runs <paramref name="work" /> in a new scope and saves the unit of work.
    /// </summary>
    public async Task InScopeAsync(Func<IServiceProvider, Task> work)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await work(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task FlushAsync<TEntity, TRepository>(List<TEntity> chunk)
        where TEntity : EntityBase
        where TRepository : IRepository<TEntity>
    {
        await InScopeAsync(async provider =>
        {
            TRepository repository = provider.GetRequiredService<TRepository>();

            foreach (TEntity entity in chunk)
                await repository.AddAsync(entity);
        });
    }
}
