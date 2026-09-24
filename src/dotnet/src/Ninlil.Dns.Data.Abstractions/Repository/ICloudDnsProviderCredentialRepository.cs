using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup a zone's own
///     cloud sync needs: given the credential a <see cref="ZoneEntity.CloudProviderCredentialId" />
///     names, find the matching, pre-configured credential set -- plus the real, pushed-down
///     paging <c>CloudProviderCredentialsController</c>'s own <c>List</c> action lists the full
///     credential set through. Like <see cref="IZoneRepository" />, this repository takes no
///     search-criteria parameter: <c>CloudProviderCredentialsController</c>'s own <c>List</c>
///     action has never accepted a filter querystring parameter of any kind, only pagination, so
///     there is no real filter axis for <see cref="SearchAsync" /> to translate.
/// </summary>
public interface ICloudDnsProviderCredentialRepository
    : IRepository<CloudDnsProviderCredentialEntity>
{
    /// <summary>
    ///     This method looks up a credential set by its exact name.
    /// </summary>
    /// <param name="name">The credential set's name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching credential set, or <see langword="null" /> if none exists.</returns>
    Task<CloudDnsProviderCredentialEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every cloud DNS provider credential set this server knows
    ///     about, ordered by name -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>
    ///     (or the MongoDB equivalent) rather than an in-memory sort/skip/take over a fully
    ///     materialized <see cref="IRepository{TEntity}.ListAsync" /> result.
    /// </summary>
    /// <param name="skip">The number of matching credential sets to skip, for paging.</param>
    /// <param name="take">The maximum number of credential sets to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of credential sets, ordered by name.</returns>
    Task<IReadOnlyList<CloudDnsProviderCredentialEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every cloud DNS provider credential set this server knows about,
    ///     without materializing them -- the total <see cref="SearchAsync" />'s own paged response
    ///     reports alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of cloud DNS provider credential sets.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
