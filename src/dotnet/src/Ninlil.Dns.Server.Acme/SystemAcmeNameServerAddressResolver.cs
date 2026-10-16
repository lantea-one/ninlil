using System.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class resolves an out-of-zone name server host through the operating system's
///     resolver (<c>System.Net.Dns.GetHostAddressesAsync</c>), the
///     default <see cref="IAcmeNameServerAddressResolver" />. The system resolver is used on
///     purpose: the name server of a delegated zone is public, and asking this process's own
///     recursive resolver would need that feature to be on.
/// </summary>
public sealed class SystemAcmeNameServerAddressResolver : IAcmeNameServerAddressResolver
{
    /// <inheritdoc />
    public Task<IPAddress[]> ResolveAsync(DnsName host, CancellationToken stoppingToken) =>
        System.Net.Dns.GetHostAddressesAsync(host.ToString().TrimEnd('.'), stoppingToken);
}
