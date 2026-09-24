using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup TSIG
///     verification needs on every signed message it processes: given the key name a message
///     claims to be signed with, find the matching, pre-shared key -- plus the real, pushed-down
///     paging <c>TsigKeysController</c>'s own <c>List</c> action lists the full key set through.
///     Like <see cref="IZoneRepository" />, this repository takes no search-criteria parameter:
///     <c>TsigKeysController</c>'s own <c>List</c> action has never accepted a filter querystring
///     parameter of any kind, only pagination, so there is no real filter axis for
///     <see cref="SearchAsync" /> to translate.
/// </summary>
public interface ITsigKeyRepository : IRepository<TsigKeyEntity>
{
    /// <summary>
    ///     This method looks up a TSIG key by its exact name.
    /// </summary>
    /// <param name="name">The key name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching key, or <see langword="null" /> if none exists.</returns>
    Task<TsigKeyEntity> FindByNameAsync(string name, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists one page of every TSIG key this server knows about, ordered by name --
    ///     a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB
    ///     equivalent) rather than an in-memory sort/skip/take over a fully materialized
    ///     <see cref="IRepository{TEntity}.ListAsync" /> result.
    /// </summary>
    /// <param name="skip">The number of matching keys to skip, for paging.</param>
    /// <param name="take">The maximum number of keys to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of keys, ordered by name.</returns>
    Task<IReadOnlyList<TsigKeyEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every TSIG key this server knows about, without materializing them --
    ///     the total <see cref="SearchAsync" />'s own paged response reports alongside its current
    ///     page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of TSIG keys.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
