using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class is the default <see cref="IForwarderHostResolver" />: it asks the operating
///     system's resolver. A server that is its own system resolver can resolve a host-name
///     forwarder only when recursion or another forwarder answers for the name.
/// </summary>
public sealed class SystemForwarderHostResolver : IForwarderHostResolver
{
    /// <inheritdoc />
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken stoppingToken) =>
        System.Net.Dns.GetHostAddressesAsync(host, stoppingToken);
}
