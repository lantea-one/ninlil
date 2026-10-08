using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IApiTokenRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class ApiTokenRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<ApiTokenEntity>(context),
        IApiTokenRepository
{
    /// <inheritdoc />
    public Task<ApiTokenEntity> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(token => token.TokenHash == tokenHash, stoppingToken);

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(
        Guid tokenId,
        DateTimeOffset consumedAtUtc,
        CancellationToken stoppingToken = default
    ) =>
        // A single conditional UPDATE: the database serializes concurrent writers, so only one
        // sees ConsumedAtUtc still null and reports one affected row. Every supported provider
        // evaluates the WHERE and the SET in the same statement.
        await Set.Where(token => token.Id == tokenId && token.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.ConsumedAtUtc, consumedAtUtc),
                stoppingToken
            ) == 1;
}
