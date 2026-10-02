using System.Text.Json;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Http;
using Ninlil.Dns.Server.Cluster.Invite;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Join;

/// <summary>
///     This class handles both ends of the join callback. On the joining node,
///     <see cref="JoinAsync" /> uses an invite code (see <see cref="Invite.ClusterInviteCode" />
///     and <see cref="ClusterInviteService" />) and completes the handshake by calling the
///     inviter's cluster channel, signed with the invite's secret, so the operator acts only on
///     the joining node. On the inviting node, <see cref="HandleJoinCompleteAsync" /> records the
///     joiner when that callback arrives.
///     <para>
///         The joining node writes nothing until the callback has succeeded, so a failed join (an
///         unreachable inviter, a rejected callback, an expired invite) leaves no key or peer row
///         behind and can be retried.
///     </para>
/// </summary>
/// <param name="clusterNodeRepository">The repository the new peer row is persisted through.</param>
/// <param name="tsigKeyRepository">The repository used to check for, and then store, the shared key.</param>
/// <param name="unitOfWork">The unit of work the new key and peer row are committed through together.</param>
/// <param name="secretProtector">The protector used to encrypt the shared secret at rest.</param>
/// <param name="httpClientFactory">The factory used to build and sign the callback request to the inviter.</param>
/// <param name="clusterOptions">This node's cluster identity and address.</param>
/// <param name="certificateProvider">The provider of this node's TLS certificate, whose thumbprint goes into the callback.</param>
public class ClusterJoinService(
    IClusterNodeRepository clusterNodeRepository,
    ITsigKeyRepository tsigKeyRepository,
    IUnitOfWork unitOfWork,
    TsigSecretProtector secretProtector,
    ClusterPeerHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ReloadableTlsCertificateProvider certificateProvider
)
{
    /// <summary>
    ///     This field contains the HMAC-SHA256 algorithm name without the trailing dot, as stored
    ///     on TSIG keys.
    /// </summary>
    private static readonly string HmacSha256AlgorithmName = DnsTsigOptions
        .HmacSha256AlgorithmName.ToString()
        .TrimEnd('.');

    /// <summary>
    ///     This method uses an invite code to join the inviting node: it checks the code, calls the
    ///     inviter's join-complete endpoint, and then stores the shared key and a peer row for the
    ///     inviter in <see cref="ClusterNodeStatusEnum.Joining" /> state until a heartbeat succeeds.
    /// </summary>
    /// <param name="inviteCode">The invite code the operator copied from the inviting node.</param>
    /// <param name="displayName">
    ///     An optional display name, used for the new peer row and sent to the inviter for its
    ///     row; without one, a name derived from the node identifier is used.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The newly created peer registry row.</returns>
    /// <exception cref="ValidationException">
    ///     Thrown when clustering is disabled or <c>AdvertisedHost</c> is not set here, the invite
    ///     code cannot be decoded, has expired or names this node, or the inviter cannot be
    ///     reached or rejects the callback.
    /// </exception>
    /// <exception cref="ConflictException">Thrown when the inviter is already a known peer or the key name already exists here.</exception>
    public async Task<ClusterNodeEntity> JoinAsync(
        string inviteCode,
        string displayName,
        CancellationToken stoppingToken = default
    )
    {
        ClusterOptions options = clusterOptions.CurrentValue;

        if (!options.Enable)
            throw new ValidationException(
                "Clustering is not enabled on this node -- set 'Cluster:Enable' before joining a peer."
            );

        if (string.IsNullOrWhiteSpace(options.AdvertisedHost))
            throw new ValidationException(
                "This node's own 'Cluster:AdvertisedHost' has not been configured -- the inviter would have no way to reach it back."
            );

        if (
            !ClusterInviteCodec.TryDecode(
                inviteCode,
                out ClusterInviteCode invite,
                out string error
            )
        )
            throw new ValidationException(error);

        if (invite.ExpiresAtUtc < DateTimeOffset.UtcNow)
            throw new ValidationException("This invite code has expired -- ask for a new one.");

        if (invite.InviterNodeId == options.NodeId)
            throw new ValidationException("A node cannot join itself.");

        if (
            await clusterNodeRepository.FindByRemoteNodeIdAsync(invite.InviterNodeId, stoppingToken)
            is not null
        )
            throw new ConflictException("This node is already a member of that peer's cluster.");

        if (await tsigKeyRepository.FindByNameAsync(invite.TsigKeyName, stoppingToken) is not null)
            throw new ConflictException(
                $"A TSIG key named '{invite.TsigKeyName}' already exists locally -- this invite cannot be consumed."
            );

        // The handshake: sign the callback with the invite's secret (not stored yet, hence
        // SendSignedWithSecretAsync) and post this node's id, address and certificate to the
        // inviter's join-complete endpoint over a client pinned to the inviter's certificate.
        // Only a success response means the inviter accepted this node.
        byte[] secretBytes = Convert.FromBase64String(invite.TsigKeySecretBase64);
        string ownThumbprint = ClusterPeerCertificateValidator.ComputeThumbprint(
            certificateProvider.Current
        );

        string callbackBody = JsonSerializer.Serialize(
            new ClusterJoinCompleteRequest(
                options.NodeId,
                options.AdvertisedHost,
                options.EffectiveAdvertisedPort,
                ownThumbprint,
                displayName
            )
        );

        using HttpClient client = httpClientFactory.CreateClient(
            invite.InviterCertificateThumbprint,
            options.HeartbeatTimeout
        );

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.SendSignedWithSecretAsync(
                client,
                $"https://{invite.InviterHost}:{invite.InviterPort}",
                "/api/cluster/channel/join-complete",
                invite.TsigKeyName,
                secretBytes,
                callbackBody,
                stoppingToken
            );
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ValidationException(
                $"Could not reach the inviting node to complete the join: {exception.Message}"
            );
        }

        if (!response.IsSuccessStatusCode)
            throw new ValidationException(
                $"The inviting node rejected the join (HTTP {(int)response.StatusCode})."
            );

        // The callback succeeded; now store the shared key and the peer row.
        TsigKeyEntity key = new()
        {
            Id = Guid.NewGuid(),
            Name = invite.TsigKeyName,
            AlgorithmName = HmacSha256AlgorithmName,
            SecretEncrypted = secretProtector.Protect(secretBytes),
            IsDisabled = false,
            Comments = $"Cluster channel key shared with peer node {invite.InviterNodeId}.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await tsigKeyRepository.AddAsync(key, stoppingToken);

        ClusterNodeEntity node = new()
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(displayName)
                ? $"node-{invite.InviterNodeId.ToString("N")[..8]}"
                : displayName,
            RemoteNodeId = invite.InviterNodeId,
            Host = invite.InviterHost,
            Port = invite.InviterPort,
            TsigKeyName = invite.TsigKeyName,
            TlsCertificateThumbprint = invite.InviterCertificateThumbprint,
            Status = ClusterNodeStatusEnum.Joining,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await clusterNodeRepository.AddAsync(node, stoppingToken);

        await unitOfWork.SaveChangesAsync(stoppingToken);

        return node;
    }

    /// <summary>
    ///     This method handles a join callback on the inviting node, after
    ///     <c>ClusterChannelController.JoinComplete</c> has authenticated it. The valid signature
    ///     is the only check: the signing key is not checked to be one an invite created, so any
    ///     enabled TSIG key (see <see cref="Authentication.ClusterTsigAuthenticationHandler" />)
    ///     can register a peer, and the invite's expiry is not checked. The new peer is recorded
    ///     as <see cref="ClusterNodeStatusEnum.Healthy" />. A repeated callback for a node id
    ///     already registered under the same key (for example after the joiner timed out)
    ///     updates the row's address, port, certificate and name instead of failing.
    /// </summary>
    /// <param name="authenticatedTsigKeyName">
    ///     The name of the TSIG key <see cref="Authentication.ClusterTsigAuthenticationHandler" />
    ///     verified the request with.
    /// </param>
    /// <param name="request">The joiner's callback body.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The peer registry row now on record for the joiner.</returns>
    /// <exception cref="ValidationException">Thrown when clustering is disabled, or the joiner claims this node's identifier.</exception>
    /// <exception cref="ConflictException">Thrown when the claimed node id is already on record under a different key.</exception>
    public async Task<ClusterNodeEntity> HandleJoinCompleteAsync(
        string authenticatedTsigKeyName,
        ClusterJoinCompleteRequest request,
        CancellationToken stoppingToken = default
    )
    {
        ClusterOptions options = clusterOptions.CurrentValue;

        if (!options.Enable)
            throw new ValidationException("Clustering is not enabled on this node.");

        if (request.JoinerNodeId == options.NodeId)
            throw new ValidationException("A node cannot join itself.");

        ClusterNodeEntity existing = await clusterNodeRepository.FindByRemoteNodeIdAsync(
            request.JoinerNodeId,
            stoppingToken
        );

        if (existing is not null)
        {
            if (
                !string.Equals(
                    existing.TsigKeyName,
                    authenticatedTsigKeyName,
                    StringComparison.Ordinal
                )
            )
                throw new ConflictException(
                    "This node id is already registered locally under a different key."
                );

            // A repeated callback: update address, certificate and name, but keep the status and
            // heartbeat history.
            existing.Host = request.JoinerHost;
            existing.Port = request.JoinerPort;
            existing.TlsCertificateThumbprint = request.JoinerCertificateThumbprint;
            if (!string.IsNullOrWhiteSpace(request.JoinerDisplayName))
                existing.Name = request.JoinerDisplayName;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

            clusterNodeRepository.Update(existing);
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return existing;
        }

        ClusterNodeEntity node = new()
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(request.JoinerDisplayName)
                ? $"node-{request.JoinerNodeId.ToString("N")[..8]}"
                : request.JoinerDisplayName,
            RemoteNodeId = request.JoinerNodeId,
            Host = request.JoinerHost,
            Port = request.JoinerPort,
            TsigKeyName = authenticatedTsigKeyName,
            TlsCertificateThumbprint = request.JoinerCertificateThumbprint,

            // The authenticated callback shows the joiner holds the key and can reach this node,
            // so the row starts Healthy rather than Joining, although no probe from this node to
            // the joiner has succeeded yet.
            Status = ClusterNodeStatusEnum.Healthy,
            LastHeartbeatAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await clusterNodeRepository.AddAsync(node, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return node;
    }
}
