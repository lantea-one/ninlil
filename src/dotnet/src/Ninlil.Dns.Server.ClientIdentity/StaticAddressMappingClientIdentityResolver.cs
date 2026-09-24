using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity;

/// <summary>
///     This class implements <see cref="IClientIdentityResolver" /> against every
///     operator-authored <see cref="DnsClientStaticAddressMappingEntity" /> row -- the manual,
///     always-available third resolver, for a device the other two real implementations
///     (<see cref="ArpNeighborTableClientIdentityResolver" />, this server's own DHCP-lease
///     resolver) can never see at all: a genuinely static, manually addressed host on a different
///     VLAN/segment/subnet this server's own ARP table or DHCP engine never observes any traffic
///     from.
///     <para>
///         <strong>Tried first, not last, in <c>Ninlil.Web/Program.cs</c>'s own composed
///         <see cref="CompositeClientIdentityResolver" /> order.</strong> An operator who
///         explicitly pins one address to one identifier has expressed a deliberate, authoritative
///         intent -- if that same address also happens to appear in this server's own ARP table or
///         DHCP lease set (a device that moved, a stale entry, a misconfigured relay), the manual
///         pin should still win, not be silently shadowed by whichever automatic signal happens to
///         answer first. Every other resolver in this chain only ever reports what this server
///         incidentally observed; this one reports what an administrator explicitly declared, which
///         is why it is consulted before either of them.
///     </para>
///     <para>
///         Mirrors <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseClientIdentityResolver</c>'s own
///         "open a fresh repository scope per call, cache nothing" shape rather than
///         <see cref="ArpNeighborTableClientIdentityResolver" />'s in-memory-snapshot one -- a
///         mapping can be added, edited, or removed through the real REST/UI surface at any
///         moment, and this resolver is not on this project's hot query-answering path, so there
///         is no performance reason to trade up-to-the-moment correctness away for a cache this
///         feature does not need. The one exception to "no caching at all" is deliberately absent
///         here too: unlike <c>DnsFilterEngine</c>'s own CIDR/DoH-ClientID matching (evaluated
///         against every query, a genuine hot path), a client-identity resolution happens at most
///         once per query and is already the least-hot of this project's three resolvers to be
///         tried, so a handful of enabled mappings scanned linearly on every call is never a real
///         cost.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach <see cref="IDnsClientStaticAddressMappingRepository" /> in a
///     scope of its own, once per call.
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

        // This project's own Plain DNS UDP listener is always a dual-stack socket bound to the
        // IPv6 wildcard address, so a real IPv4 client's own address arrives everywhere
        // downstream -- including here -- as an IPv4-mapped IPv6 address ("::ffff:x.x.x.x"), not
        // the plain dotted-quad form an operator would actually type into a mapping's own Cidr
        // field. Unmapping it first is what makes both the bare-address exact match and the CIDR
        // containment check below actually match a genuine, real-world query instead of silently
        // never resolving one at all -- the same normalization every other resolver in this chain
        // already applies.
        IPAddress normalizedAddress = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : remoteAddress;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsClientStaticAddressMappingRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientStaticAddressMappingRepository>();

        IReadOnlyList<DnsClientStaticAddressMappingEntity> mappings =
            await repository.ListEnabledAsync(stoppingToken);

        foreach (DnsClientStaticAddressMappingEntity mapping in mappings)
        {
            if (NetworkContains(mapping.Cidr, normalizedAddress))
                return new ClientIdentityResolution(
                    mapping.ClientIdentifier,
                    mapping.ProtocolVersion
                );
        }

        return null;
    }

    /// <summary>
    ///     This method checks whether a mapping's own <see cref="DnsClientStaticAddressMappingEntity.Cidr" />
    ///     -- a bare address (exact match) or a <c>/</c>-qualified CIDR network (containment) --
    ///     covers a single address. Duplicated, deliberately, from
    ///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>'s own private, identically-shaped
    ///     helper rather than shared, since that method is private to its own class and this
    ///     project has no existing shared "network-or-address matching" utility to reach for
    ///     instead -- both are small, stable, and each is more naturally read alongside its own
    ///     caller.
    /// </summary>
    private static bool NetworkContains(string entry, IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return false;

        try
        {
            if (entry.Contains('/'))
                return IPNetwork.Parse(entry).Contains(address);

            return IPAddress.Parse(entry).Equals(address);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
