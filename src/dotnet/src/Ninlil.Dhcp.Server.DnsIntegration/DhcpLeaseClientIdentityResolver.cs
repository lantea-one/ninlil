using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class implements <see cref="IClientIdentityResolver" /> against this server's own
///     live <see cref="LeaseEntity" /> table -- faster and more authoritative than an ARP/
///     neighbor-table lookup whenever this server itself is the one that actually granted the
///     lease, since it needs no OS-level cache and reflects a brand-new lease the instant it is
///     granted rather than waiting on that cache's own next refresh.
///     <para>
///         Registered only when this server's own local DHCP engine
///         (<c>Ninlil.Dhcp.Server.Core</c>) is actually active -- never when this server is
///         instead configured as an RFC 3046/RFC 8415 relay agent
///         (<c>Ninlil.Dhcp.Server.Relay</c>), which grants no lease of its own and so has no
///         <see cref="LeaseEntity" /> row for this resolver to ever find. See
///         <c>Extension.ServiceCollectionDhcpDnsIntegrationExtension.AddNinlilDhcpDnsIntegration</c>
///         and <c>Ninlil.Web/Program.cs</c>'s own composition of
///         <c>CompositeClientIdentityResolver</c>.
///     </para>
///     <para>
///         Mirrors <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>'s own "open a fresh
///         repository scope per call, cache nothing" shape rather than
///         <c>DnsAccessControlRuleCache</c>'s in-memory-snapshot one -- a lease can be granted,
///         renewed, or released at any moment, and this resolver is not (yet) on this project's
///         hot query-answering path, so there is no performance reason to trade that
///         up-to-the-moment correctness away for a cache this feature does not yet need.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach <see cref="ILeaseRepository" /> in a scope of its own, once per call.</param>
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

        // This project's own Plain DNS UDP listener is always a dual-stack socket bound to the
        // IPv6 wildcard address, so a real IPv4 client's own address arrives everywhere
        // downstream -- including here -- as an IPv4-mapped IPv6 address ("::ffff:x.x.x.x"), not
        // the plain dotted-quad form a lease's own IpAddress column stores. Unmapping it first is
        // what makes the string-equality lookup below actually match a genuine, real-world query
        // instead of silently never resolving one at all.
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
