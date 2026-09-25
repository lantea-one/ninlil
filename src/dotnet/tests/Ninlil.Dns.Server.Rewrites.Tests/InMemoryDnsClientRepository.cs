using System.Linq.Expressions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Tests;

/// <summary>
///     This class is a minimal in-memory <see cref="IDnsClientRepository" /> for the client-scoping
///     tests. Only the members <see cref="DnsRewriteEngine" /> calls to resolve a client are
///     implemented meaningfully.
/// </summary>
public sealed class InMemoryDnsClientRepository : IDnsClientRepository
{
    public readonly List<DnsClientEntity> Items = [];

    /// <summary>
    ///     This field stands in for the <c>DnsClientAddressEntity</c> table that
    ///     <see cref="GetByClientIdentifierAsync" /> joins through in production. A test fills it
    ///     (see <c>Fixture.AddClient</c>) along with adding the client to <see cref="Items" />.
    /// </summary>
    public readonly List<(
        string ClientIdentifier,
        InternetProtocolVersionEnum ProtocolVersion,
        Guid ClientId
    )> Addresses = [];

    public Task<DnsClientEntity> GetByIdAsync(Guid id, CancellationToken stoppingToken = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsClientEntity>> ListAsync(
        Expression<Func<DnsClientEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(DnsClientEntity entity, CancellationToken stoppingToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsClientEntity entity) { }

    public void Remove(DnsClientEntity entity) => Items.Remove(entity);

    public Task<DnsClientEntity> GetByClientIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    )
    {
        Guid? clientId = Addresses
            .Where(address =>
                address.ClientIdentifier == clientIdentifier
                && address.ProtocolVersion == protocolVersion
            )
            .Select(address => (Guid?)address.ClientId)
            .FirstOrDefault();

        return Task.FromResult(
            clientId is null ? null : Items.FirstOrDefault(item => item.Id == clientId)
        );
    }

    // Only the client-level filters are honored here; presence filters and last-seen ordering
    // are covered by the repository tests.
    public Task<IReadOnlyList<DnsClientEntity>> SearchAsync(
        DnsClientSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientEntity>>([
            .. Items
                .Where(item => Matches(item, criteria))
                .OrderBy(item => item.CreatedAtUtc)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountAsync(
        DnsClientSearchCriteria criteria,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.Count(item => Matches(item, criteria)));

    private static bool Matches(DnsClientEntity item, DnsClientSearchCriteria criteria) =>
        (criteria?.IsFavorite is not { } favorite || item.IsFavorite == favorite)
        && (criteria?.CreatedSinceUtc is not { } since || item.CreatedAtUtc >= since);
}
