using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.AccessControl;

/// <summary>
///     This interface is <c>Server.Core</c>'s own Data.Abstractions-free contract for the real
///     per-client/network access-control rule engine, implemented by
///     <c>Ninlil.Dns.Server.AccessControl</c> (which does depend on <c>Data.Abstractions</c>) --
///     the exact same "define the abstraction here, implement it in a dedicated
///     project that may depend on storage" shape <see cref="Filtering.IDnsFilterEngine" /> and
///     <see cref="RateLimiting.IDnsRateLimiter" /> already establish, kept consistent so
///     <c>Server.Core</c> itself never needs a repository reference.
///     <para>
///         <strong>Confirmed hot-path consumers</strong>: <c>Resolution.DnsQueryDispatcher.DispatchAsync</c> (a recursion-gate
///         check, consulted only when <c>RecursiveServerOptions.EnableRecursion</c> is otherwise
///         globally off, so a per-client grant can open recursion up without a global toggle) and
///         <c>Ninlil.Dns.Server.RateLimiting.TokenBucketDnsRateLimiter.TryAcquireAsync</c> (a
///         bypass check, consulted before spending -- never after -- a token, so an exempt
///         client's bucket is never even touched). Both call sites are on this project's hot
///         query-answering path, so this interface's single method is designed to be answerable
///         from an in-memory cache with zero synchronous database I/O once warm, modeled on
///         <c>Ninlil.Dns.Server.Authoritative.Cache.AuthoritativeZoneCache</c>'s own real
///         "load once, reload on a change event" shape, not on <see cref="Filtering.IDnsFilterEngine" />'s
///         own implementation (which, read directly, re-reads its client-group/network data from
///         real repositories on every single call -- a pattern this interface deliberately does
///         not repeat).
///     </para>
/// </summary>
public interface IDnsAccessControlEngine
{
    /// <summary>
    ///     This method checks whether a querying client's own source address is granted a
    ///     specific capability by any currently-enabled access-control rule whose own
    ///     network/CIDR contains it. Always answerable from an already-warm in-memory cache (see
    ///     this interface's own doc comment) -- a real implementation never opens a database
    ///     connection from inside this method.
    /// </summary>
    /// <param name="remoteAddress">The querying client's own source address.</param>
    /// <param name="capability">The capability being checked for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     A <see langword="bool" /> indicating whether at least one currently-enabled rule
    ///     grants <paramref name="capability" /> to <paramref name="remoteAddress" />.
    ///     <see langword="false" /> means no rule matched -- the caller's own existing global
    ///     default for that capability (e.g. <c>RecursiveServerOptions.EnableRecursion</c>) still
    ///     applies untouched; this engine only ever adds a grant, it never revokes one a global
    ///     default already allows.
    /// </returns>
    ValueTask<bool> HasCapabilityAsync(
        IPAddress remoteAddress,
        DnsAccessControlCapabilityEnum capability,
        CancellationToken stoppingToken = default
    );
}
