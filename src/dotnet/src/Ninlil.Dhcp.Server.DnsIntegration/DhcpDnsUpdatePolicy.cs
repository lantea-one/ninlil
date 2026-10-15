using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Server.Core.DnsUpdate;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class is the <see cref="IDhcpDnsUpdatePolicy" /> that
///     <c>AddNinlilDhcpDnsIntegration</c> registers in place of the default. A scope's leases
///     are registered in DNS when its <see cref="DhcpScopeEntity.DnsZoneId" /> names an existing
///     zone of an eligible type, the same test <see cref="DhcpDnsRegistrationService" /> makes for
///     each lease event (see <see cref="DhcpDnsRegistrationService.IsEligibleZoneType" />).
///     <c>Dhcpv6RequestHandler</c> uses the answer to set the S bit of its Client FQDN reply.
/// </summary>
/// <param name="zoneRepository">The repository the scope's DNS zone is read from.</param>
public sealed class DhcpDnsUpdatePolicy(IZoneRepository zoneRepository) : IDhcpDnsUpdatePolicy
{
    /// <inheritdoc />
    public async Task<bool> RegistersHostNamesAsync(
        DhcpScopeEntity scope,
        CancellationToken stoppingToken = default
    )
    {
        (ZoneEntity zone, _) = await DhcpDnsRegistrationService.FindEligibleZoneAsync(
            scope,
            zoneRepository,
            stoppingToken
        );

        return zone is not null;
    }
}
