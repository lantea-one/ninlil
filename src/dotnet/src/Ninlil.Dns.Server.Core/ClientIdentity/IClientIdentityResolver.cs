using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This interface is <c>Server.Core</c>'s own Data.Abstractions-free contract for resolving a
///     DNS query's source IP address to a persistent client identifier (a MAC address for an
///     IPv4 client, a DHCP Unique Identifier (DUID) for an IPv6 one) -- the same "define the
///     abstraction here, implement it in a dedicated project that may depend on storage" shape
///     <see cref="Filtering.IDnsFilterEngine" />, <see cref="RateLimiting.IDnsRateLimiter" />, and
///     <see cref="AccessControl.IDnsAccessControlEngine" /> already establish, kept consistent so
///     <c>Server.Core</c> itself never needs a repository reference or a hard dependency on
///     <c>Ninlil.Dhcp.*</c>.
///     <para>
///         This interface exists so a later feature -- per-client filter-list assignment, a
///         per-client activity view in the persistent query log, parental-control schedules --
///         can ask "which physical device made this query" without caring whether that device's
///         address came from a lease this server itself issued, a lease issued by some other
///         router/modem entirely, or a client with a static address and no DHCP involvement at
///         all. A real implementation resolves an address regardless of which of those is true;
///         a real deployment may run more than one implementation and fall back through them in
///         order (see <see cref="CompositeClientIdentityResolver" />).
///     </para>
///     <para>
///         <strong>Not yet a hot-path consumer of anything</strong>: this interface, and every
///         implementation of it, is defined and registered so it exists and is independently
///         provable, but nothing in <see cref="Pipeline.DnsQueryProcessingPipeline" /> consults it
///         yet -- that wiring, and the per-client filtering/parental-control features that
///         actually need it, are a later feature's own job, not this one's.
///     </para>
/// </summary>
public interface IClientIdentityResolver
{
    /// <summary>
    ///     This method resolves a querying client's own source address to its persistent client
    ///     identifier, if this resolver is able to.
    /// </summary>
    /// <param name="remoteAddress">The querying client's own source address.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The resolved identifier, or <see langword="null" /> if this resolver has no mapping for
    ///     <paramref name="remoteAddress" /> -- never an exception for the ordinary "unknown
    ///     address" case, only for a genuine, unexpected failure.
    /// </returns>
    Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    );
}
