using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This interface looks up the addresses of a forwarder's host name. <see cref="ForwarderEndpointCache" />
///     calls it from a background task, never on the query path. The default is
///     <see cref="SystemForwarderHostResolver" />; a test replaces it with a fake that decides
///     when each lookup finishes.
/// </summary>
public interface IForwarderHostResolver
{
    /// <summary>
    ///     This method resolves a host name.
    /// </summary>
    /// <param name="host">The host name from the forwarder definition.</param>
    /// <param name="stoppingToken">The token that cancels the lookup.</param>
    /// <returns>The addresses of the host, in the order the resolver returned them.</returns>
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken stoppingToken);
}
