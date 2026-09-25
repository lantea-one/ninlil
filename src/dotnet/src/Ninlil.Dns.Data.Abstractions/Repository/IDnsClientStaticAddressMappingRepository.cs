using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the bulk lookup
///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c> uses to
///     test a query's source address against every mapping, and the paged
///     <see cref="SearchAsync" />/<see cref="CountAsync" /> listing, which has no filters. It has
///     the same shape as <see cref="IDnsAccessControlRuleRepository" />.
/// </summary>
public interface IDnsClientStaticAddressMappingRepository
    : IRepository<DnsClientStaticAddressMappingEntity>
{
    /// <summary>
    ///     This method lists every enabled mapping: the set
    ///     <c>StaticAddressMappingClientIdentityResolver</c> tests a source address against on each
    ///     resolution. Leaving disabled rows out here means the resolver never has to filter them.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every mapping with <see cref="DnsClientStaticAddressMappingEntity.IsDisabled" /> <see langword="false" />.</returns>
    Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of static address mappings, enabled or not, ordered by
    ///     <see cref="DnsClientStaticAddressMappingEntity.Cidr" /> and paged in the database.
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
    ///     This method counts every static address mapping, enabled or not, for the total a paged
    ///     response reports next to <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of static address mappings.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
