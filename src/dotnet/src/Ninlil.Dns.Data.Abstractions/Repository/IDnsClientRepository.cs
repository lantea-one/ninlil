using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup that client
///     identity and auto-population use, and a paged <see cref="SearchAsync" />/<see cref="CountAsync" />
///     that applies the filters and ordering of <see cref="DnsClientSearchCriteria" /> in the
///     database.
/// </summary>
public interface IDnsClientRepository : IRepository<DnsClientEntity>
{
    /// <summary>
    ///     This method looks up the known device that answers to a persistent identifier. Identity
    ///     resolution and auto-population use it to find (or decide to create) the
    ///     <see cref="DnsClientEntity" /> for a resolved <c>ClientIdentityResolution</c>. It joins
    ///     through <see cref="Entity.DnsClientAddressEntity" />, because a device can answer to several
    ///     identifiers (a dual-stack device's IPv4 MAC address and IPv6 DUID, or identifiers an
    ///     operator linked), and it returns the owning device whichever address matched.
    /// </summary>
    /// <param name="clientIdentifier">The identifier to resolve: a MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
    /// <param name="protocolVersion">Which of the two separate identifier spaces <paramref name="clientIdentifier" /> belongs to.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The owning device, or <see langword="null" /> if no device has ever been given this identifier.</returns>
    Task<DnsClientEntity> GetByClientIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of known clients matching <paramref name="criteria" />, in
    ///     its <see cref="DnsClientSearchCriteria.SortOrder" />, filtered and paged in the database.
    ///     Presence filters and the last-seen order are evaluated against each client's
    ///     <see cref="Entity.DnsClientAddressEntity" /> rows in the same query. Every order ends with
    ///     creation time and then the identifier as tie-breakers, so page boundaries are stable.
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
    ///     This method counts the known clients matching the same filters as
    ///     <see cref="SearchAsync" />. It gives the total of a paged response and every presence count
    ///     in the client summary. <see cref="DnsClientSearchCriteria.SortOrder" /> is ignored.
    /// </summary>
    /// <param name="criteria">The same filters <see cref="SearchAsync" /> takes; <see langword="null" /> counts every client.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of matching known clients on record.</returns>
    Task<int> CountAsync(
        DnsClientSearchCriteria criteria,
        CancellationToken stoppingToken = default
    );
}
