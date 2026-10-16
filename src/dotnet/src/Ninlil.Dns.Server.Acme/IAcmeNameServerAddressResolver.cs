using System.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This interface resolves the host name of an apex NS record that lies outside the zone, so
///     <see cref="AcmeChallengePropagationWaiter" /> can probe that server.
///     <see cref="SystemAcmeNameServerAddressResolver" /> is the default; tests replace it.
/// </summary>
public interface IAcmeNameServerAddressResolver
{
    /// <summary>
    ///     This method returns the IPv4 and IPv6 addresses of a name server host.
    /// </summary>
    /// <param name="host">The name server host name.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="Exception">The host cannot be resolved; the caller skips that host.</exception>
    Task<IPAddress[]> ResolveAsync(DnsName host, CancellationToken stoppingToken);
}
