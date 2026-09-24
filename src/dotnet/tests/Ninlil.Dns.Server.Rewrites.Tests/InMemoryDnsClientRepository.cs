using System.Linq.Expressions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Tests;

/// <summary>
///     This class is a minimal, in-memory <see cref="IDnsClientRepository" /> stand-in, mirroring
///     <c>Ninlil.Dns.Server.ClientSchedule.Tests.InMemoryDnsClientRepository</c>'s own identical
///     fake -- only the handful of members <see cref="DnsRewriteEngine" />'s own client-resolution
///     path actually calls are meaningfully implemented.
/// </summary>
public sealed class InMemoryDnsClientRepository : IDnsClientRepository
{
    public readonly List<DnsClientEntity> Items = [];

    /// <summary>
    ///     This field is this fake's own minimal stand-in for the real
    ///     <c>DnsClientAddressEntity</c> table <see cref="GetByClientIdentifierAsync" /> joins
    ///     through in production -- a test populates it (see <c>Fixture.AddClient</c>) alongside
    ///     adding the owning <see cref="DnsClientEntity" /> to <see cref="Items" />.
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

    // This fake holds no address rows, so only the client-level filters are honored here; the
    // presence filters and last-seen ordering are covered against the real repositories instead.
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
