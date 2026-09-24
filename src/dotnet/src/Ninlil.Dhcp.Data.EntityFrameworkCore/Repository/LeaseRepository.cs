using Microsoft.EntityFrameworkCore;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="ILeaseRepository" /> against the shared
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class LeaseRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<LeaseEntity>(context),
        ILeaseRepository
{
    /// <inheritdoc />
    public Task<LeaseEntity> FindByClientIdentifierAsync(
        string scopeName,
        string clientIdentifier,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            lease => lease.ScopeName == scopeName && lease.ClientIdentifier == clientIdentifier,
            stoppingToken
        );

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaseEntity>> ListByScopeNameAsync(
        string scopeName,
        CancellationToken stoppingToken = default
    ) => await Set.Where(lease => lease.ScopeName == scopeName).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public Task<LeaseEntity> FindByIpAddressAsync(
        string scopeName,
        string ipAddress,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            lease => lease.ScopeName == scopeName && lease.IpAddress == ipAddress,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<LeaseEntity> FindActiveByIpAddressAsync(
        string ipAddress,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            lease => lease.IpAddress == ipAddress && lease.State == LeaseStateEnum.Active,
            stoppingToken
        );

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaseEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.OrderBy(lease => lease.ScopeName)
            .ThenBy(lease => lease.ClientIdentifier)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaseEntity>> SearchByScopeNameAsync(
        string scopeName,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(lease => lease.ScopeName == scopeName)
            .OrderBy(lease => lease.ClientIdentifier)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByScopeNameAsync(
        string scopeName,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(lease => lease.ScopeName == scopeName, stoppingToken);
}
