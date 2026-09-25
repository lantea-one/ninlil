using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This interface resolves a query's source IP address to a persistent client identifier (a
///     MAC address for IPv4, a DUID for IPv6), so per-client features can tell which device made
///     a query. It is defined here, without storage or DHCP types, and implemented in
///     <c>Ninlil.Dns.Server.ClientIdentity</c> (static mappings, ARP/neighbor table) and
///     <c>Ninlil.Dhcp.Server.DnsIntegration</c> (DHCP leases);
///     <see cref="CompositeClientIdentityResolver" /> chains them.
///     <para>
///         The filter engine (per-client filter lists), the client schedule engine (pauses and
///         schedules) and the presence flush call it on the query path. The pipeline itself
///         does not.
///     </para>
/// </summary>
public interface IClientIdentityResolver
{
    /// <summary>
    ///     This method resolves a client's source address to its persistent identifier, if this
    ///     resolver knows it.
    /// </summary>
    /// <param name="remoteAddress">The client's source address.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The resolved identifier, or <see langword="null" /> if this resolver has no mapping for
    ///     <paramref name="remoteAddress" />. An unknown address is not an error; exceptions are
    ///     only for unexpected failures.
    /// </returns>
    Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    );
}
