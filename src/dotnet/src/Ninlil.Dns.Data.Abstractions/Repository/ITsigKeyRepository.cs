using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup TSIG
///     verification makes for every signed message (the key a message names), the paged
///     listing behind <c>TsigKeysController.List</c>, which has no filters, and the unused
///     cluster invite keys the heartbeat service sweeps.
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
    ///     This method lists one page of TSIG keys, ordered by name and paged in the database.
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
    ///     This method counts every TSIG key, for the total a paged response reports next to
    ///     <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of TSIG keys.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists every key still waiting to be used by a cluster invite (those with a
    ///     <see cref="TsigKeyEntity.ClusterInviteExpiresAtUtc" />), so the caller can remove the
    ///     expired ones. There are only ever a handful.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The unused invite keys, expired or not.</returns>
    Task<IReadOnlyList<TsigKeyEntity>> ListUnusedClusterInviteKeysAsync(
        CancellationToken stoppingToken = default
    );
}
