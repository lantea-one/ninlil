using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class returns the IP addresses of this node's healthy cluster peers, so catalog sync
///     (<see cref="CatalogClusterAddressSyncService" />) and auto-provisioned zones can fill in
///     <c>ZoneEntity.NotifyAddresses</c> and <c>PrimaryServerAddresses</c> from the cluster registry
///     instead of by hand.
///     <para>
///     It reads only <see cref="IClusterNodeRepository" />, so this project does not need a
///     reference to <c>Ninlil.Dns.Server.Cluster</c>.
///     </para>
/// </summary>
/// <param name="clusterNodeRepository">The repository this resolver reads peer rows from.</param>
/// <param name="logger">The logger a failed host name lookup is recorded to.</param>
public class ClusterPeerAddressResolver(
    IClusterNodeRepository clusterNodeRepository,
    ILogger<ClusterPeerAddressResolver> logger
)
{
    /// <summary>
    ///     This method returns the address of every <see cref="ClusterNodeStatusEnum.Healthy" /> peer.
    ///     An IP literal in <see cref="ClusterNodeEntity.Host" /> is used as is; a host name is looked
    ///     up in DNS, and a failed lookup is logged and skipped rather than thrown.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The peer addresses, without duplicates, in no particular order.</returns>
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
    ///     This method resolves a peer's <see cref="ClusterNodeEntity.Host" /> to an IP literal, the
    ///     form <c>NotifyAddresses</c> and <c>PrimaryServerAddresses</c> need (they are parsed with
    ///     <c>IPAddress.TryParse</c>, never looked up).
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
            // Fully qualified because Ninlil.Dns.* hides System.Net.Dns.
            IPAddress[] resolved = await System.Net.Dns.GetHostAddressesAsync(
                peer.Host,
                stoppingToken
            );

            // Prefer IPv4 when both families are returned, so the result is stable from call to call.
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
            // One peer's failed lookup must not stop the whole pass.
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
