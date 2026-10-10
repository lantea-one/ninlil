using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
///         The enabled mappings are kept in memory as an immutable snapshot
///         (<see cref="RefreshingSnapshot{T}" />), because every query that needs a client
///         identity (filtering, rewrites, schedules, the query log) goes through this resolver
///         first, and many queries run at once. The snapshot refreshes in the background every
///         <see cref="SnapshotTimeToLive" />, and at once when <see cref="Invalidate" /> or
///         <see cref="RefreshAsync" /> is called; <c>DnsClientStaticAddressMappingsController</c>
///         does so after every change, so a mapping added through the API applies to the next
///         query. If a refresh fails, the last good snapshot stays in use. The mappings are
///         scanned in order and the first that matches wins.
///     </para>
///     <para>
///         <strong>Storage.</strong> When a read replica is configured, a reload that only follows
///         the age of the copy reads the mappings from the replica
///         (<see cref="SnapshotLoadReasonEnum.TimeToLive" />). The first load and every reload after a
///         local change read the primary, so a change saved here is never lost to a replica that
///         has not received it yet (see <see cref="SnapshotLoadScopeExtension" />).
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach <see cref="IDnsClientStaticAddressMappingRepository" /> in a
///     short-lived scope, once per refresh.
/// </param>
/// <param name="timeProvider">The clock for the snapshot age; the system clock when <see langword="null" />.</param>
/// <param name="logger">The logger refresh failures are recorded to; none when <see langword="null" />.</param>
public sealed class StaticAddressMappingClientIdentityResolver(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider = null,
    ILogger<StaticAddressMappingClientIdentityResolver> logger = null
) : IClientIdentityResolver
{
    /// <summary>
    ///     This field is how long a snapshot is used before a background refresh starts. It also
    ///     bounds how long a change made outside this process (a cluster peer, a direct database
    ///     edit) takes to apply.
    /// </summary>
    public static readonly TimeSpan SnapshotTimeToLive = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This field is the in-memory copy of the enabled mappings.
    /// </summary>
    private readonly RefreshingSnapshot<MappingSnapshot> _snapshot = new(
        (reason, token) => LoadAsync(scopeFactory, reason, token),
        new MappingSnapshot([]),
        SnapshotTimeToLive,
        timeProvider ?? TimeProvider.System,
        logger ?? (ILogger)NullLogger.Instance,
        "static address mappings"
    );

    /// <summary>
    ///     This method marks the snapshot out of date and starts a background refresh. Queries
    ///     keep using the old snapshot until it finishes.
    /// </summary>
    public void Invalidate() => _snapshot.Invalidate();

    /// <summary>
    ///     This method reloads the snapshot and completes when the new one is in place. A load
    ///     failure is logged, not thrown.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop waiting.</param>
    public Task RefreshAsync(CancellationToken stoppingToken = default) =>
        _snapshot.RefreshAsync(stoppingToken);

    /// <summary>
    ///     This method completes when no snapshot refresh is running, so tests can wait for a
    ///     background refresh without guessing at timing.
    /// </summary>
    public Task WhenIdleAsync() => _snapshot.WhenIdleAsync();

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

        MappingSnapshot snapshot = await _snapshot.GetAsync(stoppingToken);

        foreach (Mapping mapping in snapshot.Mappings)
        {
            if (ClientAddress.NetworkContains(mapping.Cidr, normalizedAddress))
                return mapping.Resolution;
        }

        return null;
    }

    /// <summary>
    ///     This method reads the enabled mappings in a short-lived scope and builds a snapshot.
    /// </summary>
    private static async Task<MappingSnapshot> LoadAsync(
        IServiceScopeFactory scopeFactory,
        SnapshotLoadReasonEnum reason,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateLoadScope(reason);
        IDnsClientStaticAddressMappingRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientStaticAddressMappingRepository>();

        IReadOnlyList<DnsClientStaticAddressMappingEntity> mappings =
            await repository.ListEnabledAsync(stoppingToken);

        return new MappingSnapshot([
            .. mappings.Select(mapping => new Mapping(
                mapping.Cidr,
                new ClientIdentityResolution(mapping.ClientIdentifier, mapping.ProtocolVersion)
            )),
        ]);
    }

    /// <summary>
    ///     This record is one enabled mapping: the address or CIDR network it covers and the
    ///     identity it gives.
    /// </summary>
    private sealed record Mapping(string Cidr, ClientIdentityResolution Resolution);

    /// <summary>
    ///     This record is the immutable list of enabled mappings, in repository order.
    /// </summary>
    private sealed record MappingSnapshot(IReadOnlyList<Mapping> Mappings);
}
