using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This interface performs one iterative resolution: following referrals from the closest
///     zone cut <see cref="Caching.IRecursiveCache" /> knows (or the root) down to an answer, as
///     RFC 1035 §7 describes. <see cref="RecursiveQueryResolver" />, the DNSSEC validator, the
///     trust anchor rollover and the prefetch service use it; <see cref="IterativeResolutionEngine" />
///     implements it.
/// </summary>
public interface IIterativeResolutionEngine
{
    /// <summary>
    ///     This method resolves one name with a new <see cref="QueryBudget" />, starting from the
    ///     cache.
    /// </summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="type">The record type to resolve.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="requestDnssec">
    ///     Whether to set the EDNS(0) DO bit on every upstream query, requesting RRSIG, NSEC,
    ///     NSEC3 and DNSKEY records with the answers.
    /// </param>
    /// <param name="clientSubnetOption">
    ///     An RFC 7871 EDNS Client Subnet option for the upstream queries for
    ///     <paramref name="name" /> and any CNAME target, but not for name server address
    ///     lookups, which are not the client's question. <see langword="null" /> (the default)
    ///     attaches none.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The resolution's outcome.</returns>
    Task<IterativeResolutionResult> ResolveAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        DnsEdnsOption clientSubnetOption = null,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method resolves one name against an existing <see cref="QueryBudget" />, so that
    ///     extra lookups (DNSSEC validation, DNS64) count against the same client query's
    ///     bounds.
    /// </summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="type">The record type to resolve.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="requestDnssec">
    ///     Whether to set the EDNS(0) DO bit on every upstream query, requesting RRSIG, NSEC,
    ///     NSEC3 and DNSKEY records with the answers.
    /// </param>
    /// <param name="budget">The existing budget to consume from.</param>
    /// <param name="forceRootStart">
    ///     Whether to start from the root name servers instead of the closest cached ancestor, for
    ///     a caller that must see every zone cut between the root and <paramref name="name" />
    ///     (DNSSEC chain building, trust anchor rollover), which the cache would otherwise skip.
    /// </param>
    /// <param name="clientSubnetOption">
    ///     An RFC 7871 EDNS Client Subnet option, as for the other
    ///     <see cref="ResolveAsync(DnsName,DnsResourceRecordTypeEnum,DnsClassEnum,bool,DnsEdnsOption,CancellationToken)" />
    ///     overload. <see langword="null" /> (the default) attaches none, which is right for
    ///     lookups that do not answer the client's question (validation, rollover checks).
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The resolution's outcome.</returns>
    Task<IterativeResolutionResult> ResolveAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        QueryBudget budget,
        bool forceRootStart,
        DnsEdnsOption clientSubnetOption = null,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method resolves one name like
    ///     <see cref="ResolveAsync(DnsName,DnsResourceRecordTypeEnum,DnsClassEnum,bool,DnsEdnsOption,CancellationToken)" />,
    ///     but always queries upstream instead of returning a fresh cached answer. Prefetching
    ///     (<c>Caching.RecursiveCachePrefetchService</c>) needs this, since its candidates are
    ///     still fresh and an ordinary resolve would return them unchanged.
    /// </summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="type">The record type to resolve.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="requestDnssec">
    ///     Whether to set the EDNS(0) DO bit on every upstream query, requesting RRSIG, NSEC,
    ///     NSEC3 and DNSKEY records with the answers.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The resolution's outcome.</returns>
    Task<IterativeResolutionResult> PrefetchAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        CancellationToken stoppingToken = default
    );
}
