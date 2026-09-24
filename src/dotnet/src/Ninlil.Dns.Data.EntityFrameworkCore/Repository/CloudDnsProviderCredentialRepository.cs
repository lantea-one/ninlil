using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="ICloudDnsProviderCredentialRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class CloudDnsProviderCredentialRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<CloudDnsProviderCredentialEntity>(context),
        ICloudDnsProviderCredentialRepository
{
    /// <inheritdoc />
    public Task<CloudDnsProviderCredentialEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(credential => credential.Name == name, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudDnsProviderCredentialEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.OrderBy(credential => credential.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
