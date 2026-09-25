using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with a lookup of a credential set
///     by name and the paged listing behind <c>CloudProviderCredentialsController.List</c>, which
///     has no filters. Zones reference a credential set through
///     <see cref="ZoneEntity.CloudProviderCredentialId" />.
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
    ///     This method lists one page of credential sets, ordered by name and paged in the
    ///     database.
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
    ///     This method counts every credential set, for the total a paged response reports next to
    ///     <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of cloud DNS provider credential sets.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
