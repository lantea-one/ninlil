using System.Linq.Expressions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Tests;

/// <summary>
///     This class is an in-memory <see cref="IDnsClientAddressRepository" />. The client
///     directory reads every row through <see cref="ListAsync" />; the other lookups are the
///     simple scans these tests never call.
/// </summary>
public sealed class InMemoryDnsClientAddressRepository : IDnsClientAddressRepository
{
    public readonly List<DnsClientAddressEntity> Items = [];

    public Task<DnsClientAddressEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsClientAddressEntity>> ListAsync(
        Expression<Func<DnsClientAddressEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientAddressEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(DnsClientAddressEntity entity, CancellationToken stoppingToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsClientAddressEntity entity) { }

    public void Remove(DnsClientAddressEntity entity) => Items.Remove(entity);

    public Task<DnsClientAddressEntity> GetByIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult(
            Items.FirstOrDefault(item =>
                item.ClientIdentifier == clientIdentifier && item.ProtocolVersion == protocolVersion
            )
        );

    public Task<IReadOnlyList<DnsClientAddressEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientAddressEntity>>([
            .. Items.Where(item => item.ClientId == clientId),
        ]);

    public Task<IReadOnlyList<DnsClientAddressEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientAddressEntity>>([
            .. Items
                .Where(item => item.ClientId == clientId)
                .OrderBy(item => item.CreatedAtUtc)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.Count(item => item.ClientId == clientId));
}
