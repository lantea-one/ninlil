using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class implements <see cref="IClientIdentityResolver" /> from the
///     <see cref="LeaseEntity" /> table: the client identifier of the active lease for a query's
///     source address. For leases this server granted it is quicker and more reliable than the
///     ARP or neighbor table, and it sees a new lease at once instead of after the next cache
///     refresh.
///     <para>
///         <c>Program.cs</c> puts it second in the <c>CompositeClientIdentityResolver</c>, after
///         static address mappings and before the ARP resolver, but only when the DHCPv4 engine is
///         local (not the relay agent, which grants no leases); see
///         <c>Extension.ServiceCollectionDhcpDnsIntegrationExtension.AddNinlilDhcpDnsIntegration</c>.
///     </para>
///     <para>
///         Like <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>, it opens a repository scope per
///         call and caches nothing, unlike <c>DnsAccessControlRuleCache</c>, so lease changes are
///         seen at once. The composite resolver is used on the query path, so this costs one
///         lease lookup per resolution. Only the lease state is checked, not its expiry time.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory that creates a scope, and an <see cref="ILeaseRepository" />, per call.</param>
public sealed class DhcpLeaseClientIdentityResolver(IServiceScopeFactory scopeFactory)
    : IClientIdentityResolver
{
    /// <inheritdoc />
    public async Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    )
    {
        if (remoteAddress == null)
            return null;

        // The DNS listeners are dual-stack sockets, so an IPv4 client arrives as an IPv4-mapped
        // IPv6 address ("::ffff:x.x.x.x"), while leases store dotted-quad strings. Unmap it so
        // the string comparison below can match.
        IPAddress normalizedAddress = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : remoteAddress;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ILeaseRepository leaseRepository =
            scope.ServiceProvider.GetRequiredService<ILeaseRepository>();

        LeaseEntity lease = await leaseRepository.FindActiveByIpAddressAsync(
            normalizedAddress.ToString(),
            stoppingToken
        );

        return lease == null
            ? null
            : new ClientIdentityResolution(lease.ClientIdentifier, lease.ProtocolVersion);
    }
}
