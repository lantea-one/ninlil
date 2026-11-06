using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Server.Authoritative.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This hosted service is the time-to-live backstop of <see cref="AuthoritativeZoneCache" />
///     and <see cref="SignedZoneCache" />. Both caches reload a zone only when
///     <c>DnsZoneChangedEvent</c> reaches this instance, and an event made on another instance
///     that shares the database never does. Every <see cref="PassInterval" /> this service
///     compares the stored zones with the cached ones and reloads each zone that differs.
/// </summary>
/// <param name="zoneCache">The zone cache to compare and reload.</param>
/// <param name="signedZoneCache">The signed-zone cache to reload with it.</param>
/// <param name="scopeFactory">The factory used to open the scope a detection pass reads from.</param>
/// <param name="timeProvider">The clock of the pass timer, so tests can drive it.</param>
/// <param name="logger">The logger for pass and reload failures.</param>
public sealed class ZoneCacheBackstopService(
    AuthoritativeZoneCache zoneCache,
    SignedZoneCache signedZoneCache,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ZoneCacheBackstopService> logger
) : BackgroundService
{
    /// <summary>
    ///     This field is the time between two passes. It matches the age limit of the other
    ///     time-to-live caches (client directory, filter configuration, rewrites, schedules and
    ///     forwarder zones).
    /// </summary>
    public static readonly TimeSpan PassInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     This property counts the passes that have finished, whether or not they found a change.
    ///     Tests wait on it.
    /// </summary>
    public long CompletedPassCount => Interlocked.Read(ref _completedPassCount);

    private long _completedPassCount;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Create the timer before the first await, so it exists once StartAsync returns.
        using PeriodicTimer timer = new(PassInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunPassAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    ///     This method runs one pass: it reads the stored zones, compares each with the cached
    ///     one and reloads the zones that were added, changed or removed.
    ///     <para>
    ///         The pass only detects. It never puts what it read into a cache: each reload reads
    ///         the zone again from the primary. A change this instance made between the
    ///         detection read and the reload therefore cannot be undone, and a zone that did not
    ///         change is not touched, so a signed zone is not signed again.
    ///     </para>
    ///     <para>
    ///         It reloads the two caches and does nothing else. It publishes no
    ///         <c>DnsZoneChangedEvent</c>, so no NOTIFY, PTR sync or webhook starts on this
    ///         instance for a change another instance already announced.
    ///     </para>
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task RunPassAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            foreach (Guid zoneId in await FindChangedZoneIdsAsync(stoppingToken))
                await ReloadAsync(zoneId, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A database outage must not end the loop; the next pass tries again.
            logger.LogError(exception, "The zone cache backstop pass failed.");
        }
        finally
        {
            Interlocked.Increment(ref _completedPassCount);
        }
    }

    /// <summary>
    ///     This method reads the stored zones and returns the ids of those that differ from the
    ///     cache.
    /// </summary>
    private async Task<List<Guid>> FindChangedZoneIdsAsync(CancellationToken stoppingToken)
    {
        // A detection read may lag behind the primary, because it only decides whether to reload
        // and the reload reads the primary (the age-reload rule of AGENTS.md "Read replicas").
        // Behind a replica a zone this instance just changed can look changed once more; the
        // extra reload is harmless.
        await using AsyncServiceScope scope = scopeFactory.CreateReplicaReadScope();

        (List<ZoneEntity> storedZones, ILookup<Guid, RecordEntity> recordsByZoneId) =
            await AuthoritativeZoneCache.ReadStoredZonesAsync(scope.ServiceProvider, stoppingToken);

        Dictionary<Guid, CachedZone> cachedById = zoneCache
            .GetCachedZones()
            .ToDictionary(cached => cached.Zone.Id);
        List<Guid> changed = [];

        foreach (ZoneEntity stored in storedZones)
        {
            if (
                !cachedById.Remove(stored.Id, out CachedZone cached)
                || !ZoneChangeDetector.IsUnchanged(cached, stored, [.. recordsByZoneId[stored.Id]])
            )
                changed.Add(stored.Id);
        }

        // What is left in the cache has no stored, enabled, answerable zone any more.
        changed.AddRange(cachedById.Keys);

        return changed;
    }

    /// <summary>
    ///     This method reloads one zone in both caches, as the event subscribers do. A failure is
    ///     logged and does not stop the other zones; the zone still differs, so the next pass
    ///     tries again.
    /// </summary>
    private async Task ReloadAsync(Guid zoneId, CancellationToken stoppingToken)
    {
        try
        {
            await zoneCache.ReloadZoneAsync(zoneId, stoppingToken);
            await signedZoneCache.ReloadZoneAsync(zoneId, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "The zone cache backstop could not reload zone {ZoneId}.",
                zoneId
            );
        }
    }
}
