using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity;

/// <summary>
///     This class implements <see cref="IClientIdentityResolver" /> from the operator's
///     <see cref="DnsClientStaticAddressMappingEntity" /> rows. It covers devices the automatic
///     resolvers (<see cref="ArpNeighborTableClientIdentityResolver" /> and the DHCP-lease
///     resolver) cannot see, such as a statically addressed host on another VLAN or subnet whose
///     traffic reaches this server only through a router.
///     <para>
///         <strong>It is tried first</strong> in the <see cref="CompositeClientIdentityResolver" />
///         chain built in <c>Ninlil.Web/Program.cs</c>. A mapping is an explicit statement by an
///         administrator, while the other resolvers report what the server happened to observe,
///         so if the same address also appears in the ARP table or a DHCP lease (a device that
///         moved, a stale entry, a misconfigured relay), the mapping wins.
///     </para>
///     <para>
///         Like <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseClientIdentityResolver</c>, it opens
///         a repository scope on every call and caches nothing, so a mapping added or changed
///         through the API applies to the next query. The enabled mappings are read and scanned
///         in order on each call; the first that matches wins. Every query that needs a client
///         identity (filtering, rewrites, schedules, the query log) goes through this resolver
///         first, so each of those checks costs a database read.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach <see cref="IDnsClientStaticAddressMappingRepository" /> in a
///     short-lived scope, once per call.
/// </param>
public sealed class StaticAddressMappingClientIdentityResolver(IServiceScopeFactory scopeFactory)
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

        // Pipeline callers already pass a normalized address; the presence flush passes stored
        // ones, so normalize again here (IPv4-mapped to IPv4, scope id dropped).
        IPAddress normalizedAddress = ClientAddress.Normalize(remoteAddress);

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsClientStaticAddressMappingRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientStaticAddressMappingRepository>();

        IReadOnlyList<DnsClientStaticAddressMappingEntity> mappings =
            await repository.ListEnabledAsync(stoppingToken);

        foreach (DnsClientStaticAddressMappingEntity mapping in mappings)
        {
            if (ClientAddress.NetworkContains(mapping.Cidr, normalizedAddress))
                return new ClientIdentityResolution(
                    mapping.ClientIdentifier,
                    mapping.ProtocolVersion
                );
        }

        return null;
    }
}
