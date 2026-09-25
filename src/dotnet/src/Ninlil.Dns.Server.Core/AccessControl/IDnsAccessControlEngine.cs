using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.AccessControl;

/// <summary>
///     This interface checks whether a client address is granted a capability by the
///     per-network access-control rules. It is defined here, without storage types, and
///     implemented by <c>Ninlil.Dns.Server.AccessControl</c>, so <c>Server.Core</c> needs no
///     repository reference (the same split as <see cref="Filtering.IDnsFilterEngine" /> and
///     <see cref="RateLimiting.IDnsRateLimiter" />).
///     <para>
///         It is called on the query path by <c>RecursiveQueryResolver</c> (to allow recursion
///         for a client while <c>RecursiveServerOptions.EnableRecursion</c> is off) and by
///         <c>TokenBucketDnsRateLimiter</c> (to exempt a client before a token is spent). So the
///         implementation answers from an in-memory rule cache that reloads on change events,
///         with no database access per call.
///     </para>
/// </summary>
public interface IDnsAccessControlEngine
{
    /// <summary>
    ///     This method checks whether any enabled access-control rule whose network contains the
    ///     client's address grants <paramref name="capability" />. Implementations answer from
    ///     memory, without a database call.
    /// </summary>
    /// <param name="remoteAddress">The client's source address.</param>
    /// <param name="capability">The capability being checked for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     Whether at least one enabled rule grants <paramref name="capability" /> to
    ///     <paramref name="remoteAddress" />. <see langword="false" /> means no rule matched and
    ///     the caller's global default (such as <c>RecursiveServerOptions.EnableRecursion</c>)
    ///     applies. Rules only add grants; they never take away what a global default allows.
    /// </returns>
    ValueTask<bool> HasCapabilityAsync(
        IPAddress remoteAddress,
        DnsAccessControlCapabilityEnum capability,
        CancellationToken stoppingToken = default
    );
}
