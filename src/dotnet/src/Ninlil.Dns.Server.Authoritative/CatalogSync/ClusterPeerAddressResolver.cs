using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class resolves the current set of DNS-facing IP addresses this node's own healthy
///     cluster peers can be reached at -- the piece that lets catalog zone auto-sync (and any
///     zone it auto-provisions) derive <c>ZoneEntity.NotifyAddresses</c>/
///     <c>PrimaryServerAddresses</c> from <c>ClusterNodeEntity</c>'s own registry instead of an
///     operator hand-maintaining either list.
///     <para>
///         Deliberately reads only <see cref="IClusterNodeRepository" /> (already available
///         wherever <c>Data.Abstractions</c> is, which this project already depends on) rather
///         than anything from <c>Ninlil.Dns.Server.Cluster</c> itself -- this project has no
///         reason to add a project reference to the cluster channel/heartbeat engine just to read
///         the peer registry those already keep current.
///     </para>
/// </summary>
/// <param name="clusterNodeRepository">The repository this resolver reads peer rows from.</param>
/// <param name="logger">The logger a peer whose own host name fails to resolve is recorded to.</param>
public class ClusterPeerAddressResolver(
    IClusterNodeRepository clusterNodeRepository,
    ILogger<ClusterPeerAddressResolver> logger
)
{
    /// <summary>
    ///     This method resolves every currently <see cref="ClusterNodeStatusEnum.Healthy" />
    ///     peer's own DNS-facing IP address. A peer whose <see cref="ClusterNodeEntity.Host" /> is
    ///     already an IP literal resolves immediately; a hostname is resolved via ordinary DNS,
    ///     with any single peer's own resolution failure logged and skipped rather than aborting
    ///     the whole call -- this method must never throw just because one peer's own name
    ///     currently fails to resolve.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every resolved peer address, deduplicated, in no particular order.</returns>
    public async Task<IReadOnlyList<string>> ResolveHealthyPeerAddressesAsync(
        CancellationToken stoppingToken = default
    )
    {
        IReadOnlyList<ClusterNodeEntity> peers = await clusterNodeRepository.ListAsync(
            peer => peer.Status == ClusterNodeStatusEnum.Healthy,
            stoppingToken
        );

        HashSet<string> addresses = [];

        foreach (ClusterNodeEntity peer in peers)
        {
            string address = await ResolveOneAsync(peer, stoppingToken);
            if (address is not null)
                addresses.Add(address);
        }

        return [.. addresses];
    }

    /// <summary>
    ///     This method resolves a single peer's own <see cref="ClusterNodeEntity.Host" /> to an
    ///     IP address literal, the shape every consumer of <c>ZoneEntity.NotifyAddresses</c>/
    ///     <c>PrimaryServerAddresses</c> in this project expects (both are parsed via
    ///     <c>IPAddress.TryParse</c>, never resolved as hostnames at query time).
    /// </summary>
    private async Task<string> ResolveOneAsync(
        ClusterNodeEntity peer,
        CancellationToken stoppingToken
    )
    {
        if (IPAddress.TryParse(peer.Host, out IPAddress literal))
            return literal.ToString();

        try
        {
            // Fully qualified: this file's own namespace tree (Ninlil.Dns.*) would otherwise
            // shadow System.Net.Dns's own short name.
            IPAddress[] resolved = await System.Net.Dns.GetHostAddressesAsync(
                peer.Host,
                stoppingToken
            );

            // Prefer an IPv4 result when both families are returned -- most homelab deployments
            // this project targets still transfer zones over IPv4 by default, and picking one
            // consistently avoids this resolver flip-flopping between two equally valid addresses
            // on every call.
            IPAddress chosen =
                resolved.FirstOrDefault(address =>
                    address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ) ?? resolved.FirstOrDefault();

            if (chosen is null)
            {
                logger.LogWarning(
                    "Cluster peer '{PeerName}' ({PeerHost}) resolved to no addresses at all.",
                    peer.Name,
                    peer.Host
                );
                return null;
            }

            return chosen.ToString();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A single peer's own transient DNS-resolution hiccup must never take down the whole
            // catalog/cluster address reconciliation pass -- the same fail-soft posture this
            // project's rate limiter and heartbeat engine already apply to their own per-item
            // failures.
            logger.LogWarning(
                exception,
                "Could not resolve cluster peer '{PeerName}''s own host '{PeerHost}'.",
                peer.Name,
                peer.Host
            );
            return null;
        }
    }
}
