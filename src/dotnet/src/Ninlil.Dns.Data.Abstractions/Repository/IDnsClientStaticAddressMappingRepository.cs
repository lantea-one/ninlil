using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the one bulk lookup
///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c> needs to
///     test a query's own source address against every configured mapping, plus the real,
///     pushed-down <see cref="SearchAsync" />/<see cref="CountAsync" /> paging shape every entity
///     added since Phase 13g's own sweep gets from its very first commit -- mirroring
///     <see cref="IDnsAccessControlRuleRepository" /> exactly, its own closest precedent (a flat,
///     top-level, CIDR-matched entity with no owning parent and no filter querystring parameter
///     beyond pagination).
/// </summary>
public interface IDnsClientStaticAddressMappingRepository
    : IRepository<DnsClientStaticAddressMappingEntity>
{
    /// <summary>
    ///     This method lists every currently-enabled mapping -- the exact set
    ///     <c>StaticAddressMappingClientIdentityResolver</c> tests a query's own source address
    ///     against on every resolution attempt. Excluding a disabled row here, rather than
    ///     filtering it out again on every single resolution, keeps this method's own result set
    ///     exactly the rows that can actually resolve anything -- the same "never consulted while
    ///     disabled" contract <see cref="DnsClientStaticAddressMappingEntity.IsDisabled" />'s own
    ///     doc comment describes.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every mapping with <see cref="DnsClientStaticAddressMappingEntity.IsDisabled" /> <see langword="false" />.</returns>
    Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every static address mapping this server knows about,
    ///     enabled or not, ordered by <see cref="DnsClientStaticAddressMappingEntity.Cidr" /> -- a
    ///     real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent)
    ///     rather than an in-memory sort/skip/take over a fully materialized
    ///     <see cref="IRepository{TEntity}.ListAsync" /> result.
    /// </summary>
    /// <param name="skip">The number of matching mappings to skip, for paging.</param>
    /// <param name="take">The maximum number of mappings to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of mappings, ordered by network.</returns>
    Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every static address mapping this server knows about, enabled or
    ///     not, without materializing them -- the total <see cref="SearchAsync" />'s own paged
    ///     response reports alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of static address mappings.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
