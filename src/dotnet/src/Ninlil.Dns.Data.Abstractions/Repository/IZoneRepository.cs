using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with a lookup of a zone by name
///     and the paged listing behind <c>ZonesController.List</c>. That endpoint has no filters, so
///     <see cref="SearchAsync" /> takes no criteria object; if it gains filters, add one as
///     <see cref="IDnsQueryLogRepository" /> does.
/// </summary>
public interface IZoneRepository : IRepository<ZoneEntity>
{
    /// <summary>
    ///     This method looks up a zone by its exact, fully-qualified name.
    /// </summary>
    /// <param name="name">The zone name to look up, without a trailing dot.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching zone, or <see langword="null" /> if none exists.</returns>
    Task<ZoneEntity> FindByNameAsync(string name, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists one page of zones, ordered by name and paged in the database.
    /// </summary>
    /// <param name="skip">The number of matching zones to skip, for paging.</param>
    /// <param name="take">The maximum number of zones to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of zones, ordered by name.</returns>
    Task<IReadOnlyList<ZoneEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every zone, for the total a paged response reports next to
    ///     <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of zones.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
