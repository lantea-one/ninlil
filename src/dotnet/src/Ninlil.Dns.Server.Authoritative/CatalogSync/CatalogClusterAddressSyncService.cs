using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class is the periodic half of the cluster's own "close the loop" design: on a
///     fixed interval (<see cref="CatalogSyncOptions.AddressReconcileInterval" />), it re-derives
///     every catalog-managed zone's own <c>NotifyAddresses</c>/<c>PrimaryServerAddresses</c> from
///     this node's own currently-healthy cluster peers, so a peer joining, leaving, or recovering
///     from an outage after a zone's own membership was already set up is reflected automatically
///     -- <see cref="CatalogZoneMembershipService" /> only merges the address set once, at the
///     moment a zone actually joins a catalog; this service is what keeps that merge from going
///     stale for the rest of the zone's life.
///     <para>
///         Three, and only three, kinds of zone are ever touched here: a
///         <see cref="ZoneTypeEnum.Catalog" /> zone's own <c>NotifyAddresses</c> (so every future
///         member addition/removal reaches every peer); a catalog *member*'s own
///         <see cref="ZoneTypeEnum.Primary" />/<see cref="ZoneTypeEnum.CloudPrimary" />
///         <c>NotifyAddresses</c> (so its own content changes reach every peer directly, not only
///         through the catalog's own slower propagation); and an auto-provisioned
///         <see cref="ZoneTypeEnum.Secondary" /> member's own <c>PrimaryServerAddresses</c> (so it
///         keeps trying every currently-known peer, not just whichever ones existed when it was
///         first auto-provisioned). A <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone's own
///         <c>PrimaryServerAddresses</c> is deliberately never touched here -- that one address
///         (which node this catalog is actually transferred from) remains the one piece of
///         catalog-related configuration an operator still sets up once, by hand, the same way
///         bootstrapping any ordinary secondary zone's own primary always has.
///     </para>
///     <para>
///         Every merge is additive-only, exactly like <see cref="CatalogZoneMembershipService" />'s
///         own -- a peer that stops being healthy, or is removed from the cluster outright, is
///         never scrubbed from an address list this service already added it to. This is a real,
///         accepted limitation, not an oversight: distinguishing
///         a peer-contributed address from an operator's own manually-added one is not something
///         <see cref="ZoneEntity.NotifyAddresses" />' plain string list can express without a
///         provenance field this class deliberately does not add.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a fresh scope for each reconciliation round.</param>
/// <param name="options">This service's own configuration, read fresh on every round.</param>
/// <param name="logger">The logger a failed round is recorded to.</param>
public class CatalogClusterAddressSyncService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<CatalogSyncOptions> options,
    ILogger<CatalogClusterAddressSyncService> logger
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(options.CurrentValue.AddressReconcileInterval);

        while (await WaitForNextTickAsync(timer, stoppingToken))
        {
            try
            {
                await RunOneRoundAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "A catalog cluster address reconciliation round failed unexpectedly."
                );
            }
        }
    }

    /// <summary>
    ///     This method waits for the timer's next tick, translating a cancelled wait's own
    ///     <see cref="OperationCanceledException" /> into a clean <see langword="false" /> so the
    ///     caller's <see langword="while" /> loop exits normally on shutdown.
    /// </summary>
    private static async Task<bool> WaitForNextTickAsync(
        PeriodicTimer timer,
        CancellationToken stoppingToken
    )
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method runs a single reconciliation pass across every zone this node hosts.
    /// </summary>
    private async Task RunOneRoundAsync(CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        ClusterPeerAddressResolver peerAddressResolver =
            scope.ServiceProvider.GetRequiredService<ClusterPeerAddressResolver>();

        IReadOnlyList<string> peerAddresses =
            await peerAddressResolver.ResolveHealthyPeerAddressesAsync(stoppingToken);

        // Nothing to reconcile against yet -- either clustering is not enabled at all, or every
        // known peer is currently unreachable; either way, leave every zone's own address list
        // exactly as it already is rather than clearing anything.
        if (peerAddresses.Count == 0)
            return;

        IReadOnlyList<ZoneEntity> zones = await zoneRepository.ListAsync(
            stoppingToken: stoppingToken
        );
        bool anyChanged = false;

        foreach (ZoneEntity zone in zones)
        {
            bool touchesNotifyAddresses =
                zone.Type == ZoneTypeEnum.Catalog
                || (
                    zone.Type is ZoneTypeEnum.Primary or ZoneTypeEnum.CloudPrimary
                    && zone.MemberOfCatalogZoneId is not null
                );
            bool touchesPrimaryServerAddresses =
                zone.Type == ZoneTypeEnum.Secondary && zone.MemberOfCatalogZoneId is not null;

            bool changed = false;

            if (
                touchesNotifyAddresses
                && MergeInto(zone.NotifyAddresses, peerAddresses, out List<string> mergedNotify)
            )
            {
                zone.NotifyAddresses = mergedNotify;
                changed = true;
            }
            else if (
                touchesPrimaryServerAddresses
                && MergeInto(
                    zone.PrimaryServerAddresses,
                    peerAddresses,
                    out List<string> mergedPrimary
                )
            )
            {
                zone.PrimaryServerAddresses = mergedPrimary;
                changed = true;
            }

            if (!changed)
                continue;

            zone.UpdatedAtUtc = DateTimeOffset.UtcNow;
            zoneRepository.Update(zone);
            anyChanged = true;
        }

        if (anyChanged)
            await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method additively merges every peer address into an existing address list.
    /// </summary>
    /// <returns><see langword="true" /> if the list actually gained a new address.</returns>
    private static bool MergeInto(
        List<string> current,
        IReadOnlyList<string> peerAddresses,
        out List<string> merged
    )
    {
        HashSet<string> set = new(current, StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (string address in peerAddresses)
            if (set.Add(address))
                changed = true;

        merged = changed ? [.. set] : current;
        return changed;
    }
}
