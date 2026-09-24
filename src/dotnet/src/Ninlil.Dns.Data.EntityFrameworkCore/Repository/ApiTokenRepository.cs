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
}
