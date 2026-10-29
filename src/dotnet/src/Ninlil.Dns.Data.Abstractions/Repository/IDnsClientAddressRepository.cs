using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups for a device's
///     addresses: which device an identifier belongs to (the join behind
///     <see cref="IDnsClientRepository.GetByClientIdentifierAsync" />), and a paged
///     <see cref="SearchByClientIdAsync" />/<see cref="CountByClientIdAsync" /> scoped to a device,
///     with the same shape as <see cref="IDnsClientScheduleRepository" />.
/// </summary>
public interface IDnsClientAddressRepository : IRepository<DnsClientAddressEntity>
{
    /// <summary>
    ///     This method looks up an address row by its identifier. It is the lookup
    ///     <see cref="IDnsClientRepository.GetByClientIdentifierAsync" /> joins through, and the
    ///     uniqueness check every write path (registering a device, or adding an address to one)
    ///     makes before inserting a row.
    /// </summary>
    /// <param name="clientIdentifier">The identifier to look up: a MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
    /// <param name="protocolVersion">Which of the two separate identifier spaces <paramref name="clientIdentifier" /> belongs to.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching address row, or <see langword="null" /> if no device has ever been given this identifier.</returns>
    Task<DnsClientAddressEntity> GetByIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every address of one device, without paging.
    /// </summary>
    /// <param name="clientId">The owning device's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching addresses.</returns>
    Task<IReadOnlyList<DnsClientAddressEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every address of several devices with one query, without paging. The
    ///     admin client list uses it to load the addresses of a whole page of devices at once,
    ///     where <see cref="ListByClientIdAsync" /> would run one query per device. The rows come
    ///     back in no particular order across devices and, within one device, in the same order
    ///     <see cref="ListByClientIdAsync" /> returns them, so a caller groups them by
    ///     <see cref="DnsClientAddressEntity.ClientId" />. The rows are not tracked for changes.
    ///     <para>
    ///         The default implementation calls <see cref="ListByClientIdAsync" /> once per device, so
    ///         a test fake that implements only the single-device method still works. The EF Core and
    ///         MongoDB repositories override it with one query.
    ///     </para>
    /// </summary>
    /// <param name="clientIds">The owning devices' identifiers. An empty collection returns an empty list without a query.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The addresses of exactly the given devices.</returns>
    async Task<IReadOnlyList<DnsClientAddressEntity>> ListByClientIdsAsync(
        IReadOnlyCollection<Guid> clientIds,
        CancellationToken stoppingToken = default
    )
    {
        List<DnsClientAddressEntity> addresses = [];
        foreach (Guid clientId in clientIds)
            addresses.AddRange(await ListByClientIdAsync(clientId, stoppingToken));

        return addresses;
    }

    /// <summary>
    ///     This method lists one page of one device's addresses, ordered by creation time and paged
    ///     in the database.
    /// </summary>
    /// <param name="clientId">The owning device's identifier.</param>
    /// <param name="skip">The number of matching addresses to skip, for paging.</param>
    /// <param name="take">The maximum number of addresses to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of addresses, ordered by creation time.</returns>
    Task<IReadOnlyList<DnsClientAddressEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts one device's addresses. It gives the total of a paged response next to
    ///     <see cref="SearchByClientIdAsync" />, and lets the delete action refuse to remove a
    ///     device's last address, since a device with no identifier could never be resolved again.
    /// </summary>
    /// <param name="clientId">The owning device's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of addresses belonging to the device.</returns>
    Task<int> CountByClientIdAsync(Guid clientId, CancellationToken stoppingToken = default);
}
