using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This interface performs a single iterative resolution: following referrals from
///     whichever zone cut <see cref="Caching.IRecursiveCache" /> already knows about (falling
///     back to the root) down to a real answer, per RFC 1035 §7.
/// </summary>
public interface IIterativeResolutionEngine
{
    /// <summary>
    ///     This method resolves a single name.
    /// </summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="type">The record type to resolve.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="requestDnssec">
    ///     Whether to set EDNS(0)'s "DNSSEC OK" bit on every upstream query this resolution
    ///     issues, requesting RRSIG/NSEC/NSEC3/DNSKEY content alongside every answer.
    /// </param>
    /// <param name="clientSubnetOption">
    ///     An RFC 7871 EDNS Client Subnet option to attach to the upstream query issued directly
    ///     for <paramref name="name" /> itself (and, if this resolution follows a CNAME, to the
    ///     query issued for its target too) -- never to a nested lookup this resolution issues
    ///     purely to resolve a name server's own address, which is internal plumbing, not the
    ///     client's own question. <see langword="null" /> (the default) attaches none.
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
    ///     This method resolves a single name against an existing <see cref="QueryBudget" />,
    ///     rather than opening a fresh one -- for a caller (the DNSSEC validation layer, most
    ///     commonly) that needs its own additional lookups, alongside an original query's, to
    ///     still count against that same client-facing query's overall bounds.
    /// </summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="type">The record type to resolve.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="requestDnssec">
    ///     Whether to set EDNS(0)'s "DNSSEC OK" bit on every upstream query this resolution
    ///     issues, requesting RRSIG/NSEC/NSEC3/DNSKEY content alongside every answer.
    /// </param>
    /// <param name="budget">The existing budget to consume from.</param>
    /// <param name="forceRootStart">
    ///     Whether to always start this specific resolution from the root zone's own name
    ///     servers, bypassing whatever closer ancestor might already be cached -- needed when a
    ///     caller must see every real zone cut between the root and <paramref name="name" />
    ///     (again, DNSSEC chain-of-trust validation, which cannot skip a step the plain iterative
    ///     walk would happily shortcut past via the cache) rather than however few hops the cache
    ///     happens to make this particular resolution take.
    /// </param>
    /// <param name="clientSubnetOption">
    ///     An RFC 7871 EDNS Client Subnet option to attach to the upstream query issued directly
    ///     for <paramref name="name" /> itself -- see this interface's own first
    ///     <see cref="ResolveAsync(DnsName,DnsResourceRecordTypeEnum,DnsClassEnum,bool,DnsEdnsOption,CancellationToken)" />
    ///     overload's own doc comment for the full reasoning. <see langword="null" /> (the
    ///     default) attaches none -- every existing caller of this overload (DNSSEC chain-of-trust
    ///     validation, RFC 5011 rollover checks) is exactly such a case, since none of those
    ///     lookups are answering the original client's own question.
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
    ///     This method resolves a single name exactly like this interface's own first
    ///     <see cref="ResolveAsync(DnsName,DnsResourceRecordTypeEnum,DnsClassEnum,bool,DnsEdnsOption,CancellationToken)" />
    ///     overload, except it always issues a genuine, fresh upstream query rather than
    ///     returning an already-cached, not-yet-expired answer unchanged. This is the one
    ///     operation that actually needs that: active cache prefetching
    ///     (<c>Caching.RecursiveCachePrefetchService</c>), which exists specifically to replace a
    ///     still-valid cache entry before it expires, not to read it back unchanged -- an ordinary
    ///     <see cref="ResolveAsync(DnsName,DnsResourceRecordTypeEnum,DnsClassEnum,bool,DnsEdnsOption,CancellationToken)" />
    ///     call against a prefetch candidate (by definition, still fresh) would just hand back the
    ///     same entry, defeating the whole point.
    /// </summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="type">The record type to resolve.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="requestDnssec">
    ///     Whether to set EDNS(0)'s "DNSSEC OK" bit on every upstream query this resolution
    ///     issues, requesting RRSIG/NSEC/NSEC3/DNSKEY content alongside every answer.
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
