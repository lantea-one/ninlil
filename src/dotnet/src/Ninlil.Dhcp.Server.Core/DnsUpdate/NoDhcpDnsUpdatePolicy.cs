using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.DnsUpdate;

/// <summary>
///     This class is the default <see cref="IDhcpDnsUpdatePolicy" />: this server registers no
///     host names, so a DHCPv6 Client FQDN reply says S=0, N=1. The DHCP engine registrations add
///     it with <c>TryAdd</c>, and <c>AddNinlilDhcpDnsIntegration</c> replaces it, so the answer
///     does not depend on the order of the two calls.
/// </summary>
public sealed class NoDhcpDnsUpdatePolicy : IDhcpDnsUpdatePolicy
{
    /// <inheritdoc />
    public Task<bool> RegistersHostNamesAsync(
        DhcpScopeEntity scope,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(false);
}
