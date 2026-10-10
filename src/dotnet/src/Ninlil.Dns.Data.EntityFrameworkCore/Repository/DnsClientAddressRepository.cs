using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientAddressRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsClientAddressRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsClientAddressEntity>(context),
        IDnsClientAddressRepository
{
    /// <inheritdoc />
    public Task<DnsClientAddressEntity> GetByIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            address =>
                address.ClientIdentifier == clientIdentifier
                && address.ProtocolVersion == protocolVersion,
            stoppingToken
        );

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientAddressEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) => await Set.Where(address => address.ClientId == clientId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientAddressEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .Where(address => address.ClientId == clientId)
            .OrderBy(address => address.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(address => address.ClientId == clientId, stoppingToken);
}
