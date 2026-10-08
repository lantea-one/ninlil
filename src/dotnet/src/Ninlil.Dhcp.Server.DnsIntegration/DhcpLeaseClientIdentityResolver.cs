using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;

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
///         The composite resolver is used on the query path while many queries run at once, so
///         the active leases are kept in memory as an immutable snapshot
///         (<see cref="RefreshingSnapshot{T}" />) from <see cref="ILeaseRepository.ListActiveAsync" />
///         instead of being read per query. The snapshot refreshes in the background every
///         <see cref="SnapshotTimeToLive" />, and at once when
///         <see cref="DhcpLeaseIdentitySnapshotInvalidator" /> sees a
///         <see cref="DhcpLeaseChangedEvent" />; in broker mode that event is local only, so the
///         time-to-live is the backstop. A lease whose expiry time has passed is ignored even if
///         the snapshot still holds it, so a lease stops resolving the moment it expires. If a
///         refresh fails, the last good snapshot stays in use.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory that creates a scope, and an <see cref="ILeaseRepository" />, per refresh.</param>
/// <param name="timeProvider">The clock for the snapshot age and lease expiry; the system clock when <see langword="null" />.</param>
/// <param name="logger">The logger refresh failures are recorded to; none when <see langword="null" />.</param>
public sealed class DhcpLeaseClientIdentityResolver(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider = null,
    ILogger<DhcpLeaseClientIdentityResolver> logger = null
) : IClientIdentityResolver
{
    /// <summary>
    ///     This field is how long a snapshot is used before a background refresh starts.
    /// </summary>
    public static readonly TimeSpan SnapshotTimeToLive = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This field is the clock for the snapshot age and for lease expiry.
    /// </summary>
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    ///     This field is the in-memory copy of the active leases.
    /// </summary>
    private readonly RefreshingSnapshot<LeaseSnapshot> _snapshot = new(
        token => LoadAsync(scopeFactory, timeProvider ?? TimeProvider.System, token),
        new LeaseSnapshot(new Dictionary<IPAddress, ActiveLease>()),
        SnapshotTimeToLive,
        timeProvider ?? TimeProvider.System,
        logger ?? (ILogger)NullLogger.Instance,
        "active DHCP leases"
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

        // Leases are keyed by their normalized address. Pipeline callers already pass a
        // normalized address; the presence flush passes stored ones, so normalize again here.
        IPAddress normalizedAddress = ClientAddress.Normalize(remoteAddress);

        LeaseSnapshot snapshot = await _snapshot.GetAsync(stoppingToken);

        // The snapshot can be up to its time-to-live old, so check expiry here.
        if (
            !snapshot.Leases.TryGetValue(normalizedAddress, out ActiveLease lease)
            || (lease.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= _timeProvider.GetUtcNow())
        )
            return null;

        return lease.Resolution;
    }

    /// <summary>
    ///     This method reads the active leases in a short-lived scope and indexes them by address.
    ///     A lease whose address does not parse is skipped. If two leases hold one address (an
    ///     IPv4 and an IPv6 scope cannot, but two scopes can), the first read wins.
    /// </summary>
    private static async Task<LeaseSnapshot> LoadAsync(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ILeaseRepository leaseRepository =
            scope.ServiceProvider.GetRequiredService<ILeaseRepository>();

        IReadOnlyList<LeaseEntity> leases = await leaseRepository.ListActiveAsync(
            timeProvider.GetUtcNow(),
            stoppingToken
        );

        Dictionary<IPAddress, ActiveLease> byAddress = [];
        foreach (LeaseEntity lease in leases)
        {
            if (!IPAddress.TryParse(lease.IpAddress, out IPAddress address))
                continue;

            byAddress.TryAdd(
                ClientAddress.Normalize(address),
                new ActiveLease(
                    new ClientIdentityResolution(lease.ClientIdentifier, lease.ProtocolVersion),
                    lease.LeaseExpiresAtUtc
                )
            );
        }

        return new LeaseSnapshot(byAddress);
    }

    /// <summary>
    ///     This record is one active lease: the identity it gives and when it expires.
    /// </summary>
    private sealed record ActiveLease(
        ClientIdentityResolution Resolution,
        DateTimeOffset? ExpiresAtUtc
    );

    /// <summary>
    ///     This record is the immutable index of active leases by address. The dictionary is not
    ///     changed after it is built, so concurrent readers need no lock.
    /// </summary>
    private sealed record LeaseSnapshot(IReadOnlyDictionary<IPAddress, ActiveLease> Leases);
}
