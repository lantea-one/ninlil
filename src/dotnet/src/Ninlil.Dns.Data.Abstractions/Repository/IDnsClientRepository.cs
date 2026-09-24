using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup a client
///     identity/auto-population feature needs, plus the same real, pushed-down
///     <see cref="SearchAsync" />/<see cref="CountAsync" /> paging shape every paged entity in this
///     project shares (see
///     <see cref="IDnsClientGroupRepository" /> for the closest precedent), with the favorites,
///     presence, and creation-time filters and the ordering of <see cref="DnsClientSearchCriteria" />
///     translated into the same pushed-down query.
/// </summary>
public interface IDnsClientRepository : IRepository<DnsClientEntity>
{
    /// <summary>
    ///     This method looks up the known device currently answering to a given persistent
    ///     identifier -- the lookup every identity-resolution/auto-population feature needs to
    ///     find (or decide it must create) the owning <see cref="DnsClientEntity" /> row for a
    ///     resolved <c>ClientIdentityResolution</c>. Implemented as a join through
    ///     <see cref="Entity.DnsClientAddressEntity" />, since the identifier/protocol pair itself
    ///     is no longer carried directly on <see cref="DnsClientEntity" /> -- a device may answer
    ///     to more than one identifier at once (a dual-stack device's separate IPv4 MAC address and
    ///     IPv6 DUID, or two identifiers an operator has explicitly linked together), and this
    ///     lookup finds the one owning device regardless of which of its own addresses matched.
    /// </summary>
    /// <param name="clientIdentifier">The identifier to resolve: a MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
    /// <param name="protocolVersion">Which of the two disjoint identifier spaces <paramref name="clientIdentifier" /> belongs to.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The owning device, or <see langword="null" /> if no device has ever been given this identifier.</returns>
    Task<DnsClientEntity> GetByClientIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of the known clients matching <paramref name="criteria" />,
    ///     in its requested <see cref="DnsClientSearchCriteria.SortOrder" /> -- a real, translated
    ///     <c>WHERE</c>/<c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent)
    ///     rather than an in-memory filter/sort/skip/take over
    ///     <see cref="IRepository{TEntity}.ListAsync" />'s own full result. Presence filters and the
    ///     last-seen ordering are evaluated against each client's own
    ///     <see cref="Entity.DnsClientAddressEntity" /> rows inside that same query. Every ordering
    ///     ends with creation time and then the identifier as tie-breakers, so a page boundary is
    ///     always deterministic.
    /// </summary>
    /// <param name="criteria">The filters and ordering to apply; <see langword="null" /> is treated as <see cref="DnsClientSearchCriteria.All" />.</param>
    /// <param name="skip">The number of matching clients to skip, for paging.</param>
    /// <param name="take">The maximum number of clients to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of clients, in the requested order.</returns>
    Task<IReadOnlyList<DnsClientEntity>> SearchAsync(
        DnsClientSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every known client matching the same filters as
    ///     <see cref="SearchAsync" /> without materializing them -- the total its own paged
    ///     response reports alongside its current page, and the source of every presence count the
    ///     client summary reports. <see cref="DnsClientSearchCriteria.SortOrder" /> is ignored.
    /// </summary>
    /// <param name="criteria">The same filters <see cref="SearchAsync" /> takes; <see langword="null" /> counts every client.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of matching known clients on record.</returns>
    Task<int> CountAsync(
        DnsClientSearchCriteria criteria,
        CancellationToken stoppingToken = default
    );
}
