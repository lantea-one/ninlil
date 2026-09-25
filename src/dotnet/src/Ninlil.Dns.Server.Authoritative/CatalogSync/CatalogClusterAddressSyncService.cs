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
///     This hosted service, every <see cref="CatalogSyncOptions.AddressReconcileInterval" />, adds
///     the addresses of this node's healthy cluster peers to the address lists of catalog-managed
///     zones, so peers that join or recover later are included.
///     <see cref="CatalogZoneMembershipService" /> merges the addresses only once, when a zone joins
///     a catalog; this keeps them current afterwards.
///     <para>
///     It touches three kinds of zone: a <see cref="ZoneTypeEnum.Catalog" /> zone's
///     <c>NotifyAddresses</c> (so member changes reach every peer); a catalog member's
///     <c>NotifyAddresses</c> when it is <see cref="ZoneTypeEnum.Primary" /> or
///     <see cref="ZoneTypeEnum.CloudPrimary" /> (so its changes reach peers directly); and an
///     auto-provisioned <see cref="ZoneTypeEnum.Secondary" /> member's
///     <c>PrimaryServerAddresses</c> (so it can pull from any current peer). A
///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone's <c>PrimaryServerAddresses</c> is left
///     alone: which node the catalog comes from is set by hand, as for any secondary.
///     </para>
///     <para>
///     Merges only add. A peer that becomes unhealthy or leaves the cluster is never removed
///     from a list, because a plain string list cannot tell a peer-added address from one an
///     operator added.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope for each round.</param>
/// <param name="options">The interval, read on every round.</param>
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
    ///     This method waits for the next tick and returns <see langword="false" /> instead of
    ///     throwing when cancelled, so the loop ends cleanly on shutdown.
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
    ///     This method runs one pass over every zone this node hosts.
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

        // No peer addresses (clustering off, or every peer unreachable): leave every list as it
        // is rather than clearing anything.
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
    ///     This method adds any missing peer addresses to an address list.
    /// </summary>
    /// <returns>Whether any address was added.</returns>
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
