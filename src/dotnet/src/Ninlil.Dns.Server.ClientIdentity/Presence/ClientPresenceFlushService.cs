using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Identity;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Presence;

/// <summary>
///     This background service periodically drains <see cref="ClientPresenceTracker" /> and
///     persists every buffered sighting onto the known-client address it belongs to, advancing
///     that address's <see cref="DnsClientAddressEntity.LastSeenUtc" /> (and, when the
///     sighting's address family matches, its <see cref="DnsClientAddressEntity.Address" />).
///     <para>
///         Each kind of sighting is matched differently. An identity sighting matches the address
///         with the same identifier and protocol version. A hardware-address sighting matches every
///         address whose identifier embeds that MAC -- a plain MAC for an IPv4 identity, or a
///         DUID-LLT/DUID-LL for an IPv6 one (see <see cref="DhcpClientIdentifierLinkKeys" />) -- so
///         an IPv6 neighbor-table entry still marks a device registered only by its IPv4 MAC. A
///         source-address sighting is first resolved to an identity through
///         <see cref="IClientIdentityResolver" />, once per distinct address per flush rather than
///         once per query. The tracker caps that buffer at
///         <see cref="ClientPresenceTracker.MaxSourceAddressesPerFlush" /> addresses, so a flush
///         resolves a bounded number; when addresses were refused, the flush logs one warning
///         with the count. Only already-known addresses are ever updated; a sighting of an unknown
///         device is dropped, since presence tracking never registers devices itself.
///     </para>
///     <para>
///         Matching loads every stored address once per flush and only when there is something to
///         flush -- appropriate for this project's homelab scale of tens to hundreds of devices. A
///         sighting never moves <see cref="DnsClientAddressEntity.LastSeenUtc" /> backwards, and never
///         touches <see cref="AuditableEntityBase.UpdatedAtUtc" />, which records configuration
///         edits rather than observations. The save raises no entity-change signal
///         (<see cref="EntityChangeSuppression" />), so it does not make
///         <see cref="DnsClientDirectory" /> reload.
///     </para>
///     <para>
///         On shutdown the sightings still buffered are flushed once: by the loop when it ran, and
///         by <see cref="StopAsync" /> when the host stopped before the loop started.
///     </para>
/// </summary>
/// <param name="tracker">The buffer sightings are drained from.</param>
/// <param name="scopeFactory">The factory each flush opens a new scope from.</param>
/// <param name="options">The live presence options (flush interval).</param>
/// <param name="logger">The logger flush failures are recorded to.</param>
public sealed class ClientPresenceFlushService(
    ClientPresenceTracker tracker,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ClientPresenceOptions> options,
    ILogger<ClientPresenceFlushService> logger
) : BackgroundService
{
    /// <summary>
    ///     This field lists every address family a hardware sighting can be keyed under.
    /// </summary>
    private static readonly AddressFamily[] SightingFamilies =
    [
        AddressFamily.InterNetwork,
        AddressFamily.InterNetworkV6,
        AddressFamily.Unspecified,
    ];

    /// <summary>
    ///     This field says whether the loop or the stop makes the final flush.
    ///     <see cref="ExecuteAsync" /> and <see cref="StopAsync" /> each try to move it away from
    ///     <see cref="NotStarted" /> with
    ///     <see cref="Interlocked.CompareExchange(ref int, int, int)" />, and only the winner
    ///     flushes at shutdown.
    /// </summary>
    private int _state = NotStarted;

    /// <summary>
    ///     This constant is <see cref="_state" /> before the loop or the stop has claimed the final
    ///     flush.
    /// </summary>
    private const int NotStarted = 0;

    /// <summary>
    ///     This constant is <see cref="_state" /> once <see cref="ExecuteAsync" /> owns the flush.
    /// </summary>
    private const int Running = 1;

    /// <summary>
    ///     This constant is <see cref="_state" /> once <see cref="StopAsync" /> made the final flush
    ///     because the loop never ran.
    /// </summary>
    private const int FlushedByStop = 2;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The host can cancel the stopping token before a thread-pool thread runs this method;
        // StopAsync then makes the final flush, and this call must not run a second one.
        if (Interlocked.CompareExchange(ref _state, Running, NotStarted) != NotStarted)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.CurrentValue.FlushInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Unhandled exception while persisting client presence.");
            }
        }

        await FlushOnShutdownAsync();
    }

    /// <summary>
    ///     This method stops the service and, when the loop never started, persists the buffered
    ///     sightings, so a host that stops right after it started loses none.
    /// </summary>
    /// <param name="cancellationToken">The token that bounds the wait for the loop to end.</param>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (Interlocked.CompareExchange(ref _state, FlushedByStop, NotStarted) == NotStarted)
            await FlushOnShutdownAsync();
    }

    /// <summary>
    ///     This method persists whatever arrived since the last flush, on a fresh token because the
    ///     stopping one is already cancelled. A failure is logged, never thrown, so shutdown goes on.
    /// </summary>
    private async Task FlushOnShutdownAsync()
    {
        try
        {
            await FlushAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not persist client presence during shutdown.");
        }
    }

    /// <summary>
    ///     This method drains every buffered sighting and persists it onto the matching known
    ///     addresses.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of address rows whose presence was advanced.</returns>
    public async Task<int> FlushAsync(CancellationToken stoppingToken = default)
    {
        ClientPresenceSnapshot snapshot = tracker.Drain();
        if (snapshot.DroppedSourceAddresses > 0)
            logger.LogWarning(
                "Client presence buffer was full: {DroppedCount} new source addresses were not recorded since the last flush (limit {Limit}). Possible spoofed-source flood.",
                snapshot.DroppedSourceAddresses,
                ClientPresenceTracker.MaxSourceAddressesPerFlush
            );

        if (snapshot.IsEmpty)
            return 0;

        // A fresh scope per flush, so every read observes every write committed since the last
        // one rather than a stale tracked instance from an earlier scope.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;

        // Fold resolved source-address sightings into the identity sightings.
        Dictionary<(InternetProtocolVersionEnum, string), Sighting> identities = new(
            snapshot.Identities
        );
        IClientIdentityResolver resolver = services.GetService<IClientIdentityResolver>();
        if (resolver is not null)
        {
            foreach ((IPAddress address, Sighting sighting) in snapshot.SourceAddresses)
            {
                ClientIdentityResolution resolution = await resolver.ResolveAsync(
                    address,
                    stoppingToken
                );
                if (resolution is null || string.IsNullOrWhiteSpace(resolution.ClientIdentifier))
                    continue;

                (InternetProtocolVersionEnum, string) key = (
                    resolution.ProtocolVersion,
                    resolution.ClientIdentifier.Trim().ToLowerInvariant()
                );
                if (
                    !identities.TryGetValue(key, out Sighting existing)
                    || sighting.SeenAtUtc > existing.SeenAtUtc
                )
                    identities[key] = sighting;
            }
        }

        IDnsClientAddressRepository addressRepository =
            services.GetRequiredService<IDnsClientAddressRepository>();
        int advanced = 0;

        foreach (
            DnsClientAddressEntity row in await addressRepository.ListAsync(
                stoppingToken: stoppingToken
            )
        )
        {
            // Gather every sighting that belongs to this address: its identity, plus any
            // hardware sighting (of either address family) of a MAC its identifier embeds.
            List<Sighting> candidates = [];

            if (
                row.ClientIdentifier is not null
                && identities.TryGetValue(
                    (row.ProtocolVersion, row.ClientIdentifier.Trim().ToLowerInvariant()),
                    out Sighting identitySighting
                )
            )
                candidates.Add(identitySighting);

            if (snapshot.HardwareAddresses.Count > 0)
                foreach (string key in LinkKeysFor(row))
                    if (key.StartsWith("mac:", StringComparison.Ordinal))
                        foreach (AddressFamily family in SightingFamilies)
                            if (
                                snapshot.HardwareAddresses.TryGetValue(
                                    (key[4..], family),
                                    out Sighting hardware
                                )
                            )
                                candidates.Add(hardware);

            if (candidates.Count == 0)
                continue;

            // Presence advances to the newest sighting of any kind, and never moves backwards.
            Sighting newest = candidates.MaxBy(candidate => candidate.SeenAtUtc);
            if (row.LastSeenUtc is { } last && newest.SeenAtUtc <= last)
                continue;

            row.LastSeenUtc = newest.SeenAtUtc;

            // The recorded address comes from the newest sighting of the row's family only: an
            // IPv6 sighting of a device's MAC must never overwrite its IPv4 identity's address.
            Sighting sameFamily = candidates
                .Where(candidate =>
                    candidate.Address?.AddressFamily == FamilyOf(row.ProtocolVersion)
                )
                .MaxBy(candidate => candidate.SeenAtUtc);
            if (sameFamily is not null)
                row.Address = sameFamily.Address.ToString();

            addressRepository.Update(row);
            advanced++;
        }

        if (advanced > 0)
        {
            // A sighting changes only the last-seen time and the address text, and no cache reads
            // those. Without this scope each flush would make the next DNS query wait for a
            // reload of DnsClientDirectory.
            using IDisposable quiet = EntityChangeSuppression.Begin(typeof(DnsClientAddressEntity));
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(stoppingToken);
        }

        return advanced;
    }

    /// <summary>
    ///     This method derives a stored address's link keys from its identifier, by protocol
    ///     version.
    /// </summary>
    private static IReadOnlySet<string> LinkKeysFor(DnsClientAddressEntity row) =>
        row.ProtocolVersion == InternetProtocolVersionEnum.IPv6
            ? DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier(row.ClientIdentifier)
            : DhcpClientIdentifierLinkKeys.FromDhcpv4Identifier(row.ClientIdentifier);

    /// <summary>
    ///     This method maps a protocol version onto its socket address family.
    /// </summary>
    private static AddressFamily FamilyOf(InternetProtocolVersionEnum protocolVersion) =>
        protocolVersion == InternetProtocolVersionEnum.IPv6
            ? AddressFamily.InterNetworkV6
            : AddressFamily.InterNetwork;
}
