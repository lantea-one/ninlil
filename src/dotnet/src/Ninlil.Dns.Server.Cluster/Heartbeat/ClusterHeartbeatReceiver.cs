using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Channel;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Heartbeat;

/// <summary>
///     This class handles a peer's heartbeat on the receiving node, after
///     <c>ClusterChannelController.Heartbeat</c> has authenticated it with that peer's key. It
///     keeps the certificate pin current: a peer whose TLS certificate is renewed (an ACME
///     certificate every couple of months, say) announces the new thumbprint in its heartbeat,
///     and this node stores it in <see cref="ClusterNodeEntity.TlsCertificateThumbprint" /> so
///     its own probes of that peer keep passing <see cref="Http.ClusterPeerCertificateValidator" />.
///     Trusting the announcement is safe because only the holder of the peer's shared key can
///     sign the heartbeat. Health itself is still judged only by this node's own probes (see
///     <see cref="ClusterHeartbeatBackgroundService" />).
/// </summary>
/// <param name="clusterNodeRepository">The repository the peer's row is read and updated through.</param>
/// <param name="unitOfWork">The unit of work a re-pin is committed through.</param>
/// <param name="logger">The logger a re-pin is recorded to.</param>
public class ClusterHeartbeatReceiver(
    IClusterNodeRepository clusterNodeRepository,
    IUnitOfWork unitOfWork,
    ILogger<ClusterHeartbeatReceiver> logger
)
{
    /// <summary>
    ///     This method checks one heartbeat and re-pins the sender's certificate if it changed.
    /// </summary>
    /// <param name="authenticatedRemoteNodeId">
    ///     The node id of the peer the signing key belongs to, from the identity
    ///     <see cref="Authentication.ClusterTsigAuthenticationHandler" /> built.
    /// </param>
    /// <param name="request">The heartbeat body.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="ValidationException">
    ///     Thrown when the body names a different node than the signing key belongs to.
    /// </exception>
    public async Task HandleHeartbeatAsync(
        Guid authenticatedRemoteNodeId,
        ClusterHeartbeatRequest request,
        CancellationToken stoppingToken = default
    )
    {
        if (request.SenderNodeId != authenticatedRemoteNodeId)
            throw new ValidationException(
                "The heartbeat's sender does not match the peer its signing key belongs to."
            );

        string announced = request.SenderCertificateThumbprint?.ToLowerInvariant();
        if (!IsThumbprint(announced))
            return;

        ClusterNodeEntity peer = await clusterNodeRepository.FindByRemoteNodeIdAsync(
            authenticatedRemoteNodeId,
            stoppingToken
        );
        if (peer is null || peer.TlsCertificateThumbprint == announced)
            return;

        logger.LogInformation(
            "Peer node '{PeerName}' announced a new TLS certificate ({Thumbprint}); pinning it.",
            peer.Name,
            announced
        );

        peer.TlsCertificateThumbprint = announced;
        peer.UpdatedAtUtc = DateTimeOffset.UtcNow;
        clusterNodeRepository.Update(peer);
        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method checks that a value has the stored thumbprint form: 64 lowercase hex
    ///     digits, a SHA-256 hash.
    /// </summary>
    private static bool IsThumbprint(string value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
}
