using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.DnsUpdate;

/// <summary>
///     This interface answers whether this server registers the host names of a scope's leases in
///     DNS. <see cref="Dhcpv6RequestHandler" /> asks it before it sets the S bit of the RFC 4704
///     Client FQDN reply, because S=1 promises that the server updates the client's AAAA record
///     (RFC 4704 Section 4.1). The engine has no reference to the DNS projects, so the answer comes
///     from this interface: <see cref="NoDhcpDnsUpdatePolicy" /> (always false) is the default
///     and <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpDnsUpdatePolicy</c> replaces it when the
///     DHCP-to-DNS integration is registered.
/// </summary>
public interface IDhcpDnsUpdatePolicy
{
    /// <summary>
    ///     This method reports whether a lease granted from the scope gets its host name registered
    ///     in DNS by this server.
    /// </summary>
    /// <param name="scope">The scope that grants the lease.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> if this server registers the name, otherwise <see langword="false" />.</returns>
    Task<bool> RegistersHostNamesAsync(
        DhcpScopeEntity scope,
        CancellationToken stoppingToken = default
    );
}
