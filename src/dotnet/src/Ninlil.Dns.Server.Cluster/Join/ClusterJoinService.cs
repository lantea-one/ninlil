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
///     This class consumes a cluster invite code: the joining-side half of Phase 9-6a's join
///     handshake -- see <see cref="Invite.ClusterInviteCode" />'s own doc comment for the full
///     design, and <see cref="ClusterInviteService" /> for the inviting-side half. Joining is a
///     single operator action on the joining node alone: no further round trip back to the
///     inviter's own operator is needed, because <see cref="JoinAsync" /> itself completes the
///     handshake by calling back to the inviter over the very channel this sub-phase builds,
///     authenticated with the shared secret the invite carried.
///     <para>
///         Nothing is written to this node's own storage until that callback has actually
///         succeeded: a join that fails partway through (an unreachable inviter, a rejected
///         callback, an expired invite) leaves no trace in this node's own TSIG key or cluster
///         node registries, so a failed attempt can always be retried cleanly with a fresh invite.
///     </para>
/// </summary>
/// <param name="clusterNodeRepository">The repository the new peer row is persisted through.</param>
/// <param name="tsigKeyRepository">The repository used both to check for (and then persist) the shared key locally.</param>
/// <param name="unitOfWork">The unit of work the new key and peer row are committed through together.</param>
/// <param name="secretProtector">The protector used to encrypt the shared secret at rest, locally.</param>
/// <param name="httpClientFactory">The factory used to build and sign the callback request to the inviter.</param>
/// <param name="clusterOptions">This node's own cluster identity/address.</param>
/// <param name="certificateProvider">The provider this node's own cluster channel TLS certificate is read from.</param>
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
    ///     This property contains the canonical, dot-stripped presentation form of the only MAC
    ///     algorithm Ninlil implements.
    /// </summary>
    private static readonly string HmacSha256AlgorithmName = DnsTsigOptions
        .HmacSha256AlgorithmName.ToString()
        .TrimEnd('.');

    /// <summary>
    ///     This method consumes an invite code, completing the join handshake against the
    ///     inviting node.
    /// </summary>
    /// <param name="inviteCode">The opaque invite code an operator copied from the inviting node.</param>
    /// <param name="displayName">
    ///     An optional, operator-chosen display name for the new peer row this creates -- falls
    ///     back to a name derived from the inviter's own node identifier when omitted.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The newly created peer registry row.</returns>
    /// <exception cref="ValidationException">
    ///     Thrown when clustering is disabled or unconfigured locally, the invite code cannot be
    ///     decoded, has expired, names this node itself, or the inviter's own callback rejects the
    ///     join.
    /// </exception>
    /// <exception cref="ConflictException">Thrown when this inviter is already a known peer.</exception>
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

        // What/why/how: this is the actual handshake round trip -- this node signs its own
        // callback with the invite's raw secret (never yet persisted locally, so
        // SendSignedWithSecretAsync is used rather than the ordinary by-name signing path) and
        // posts its own identity/address/certificate back to the inviter's own
        // /join-complete endpoint, over a client pinned to the inviter's own certificate
        // thumbprint. Only a success response here proves the inviter actually accepted this
        // node as a peer; nothing is written locally until that happens (per this class's own
        // doc comment).
        byte[] secretBytes = Convert.FromBase64String(invite.TsigKeySecretBase64);
        string ownThumbprint = ClusterPeerCertificateValidator.ComputeThumbprint(
            certificateProvider.Current
        );

        string callbackBody = JsonSerializer.Serialize(
            new ClusterJoinCompleteRequest(
                options.NodeId,
                options.AdvertisedHost,
                options.AdvertisedPort,
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

        // The callback succeeded -- only now persist the shared key and the new peer row, exactly
        // as this class's own doc comment promises.
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
    ///     This method handles the inviting node's own side of a join callback (see
    ///     <c>ClusterChannelController.JoinComplete</c>, which authenticates the request before
    ///     ever calling this): the caller has already proven, by successfully signing this
    ///     request with the shared key this node itself generated for this exact invite, that it
    ///     is the intended joiner -- nothing further is required to record it as a trusted peer.
    ///     Idempotent: a retried callback for a node id already on record (the joiner's own
    ///     request timed out locally but actually succeeded, for example) refreshes that row's
    ///     address/certificate/name rather than failing.
    /// </summary>
    /// <param name="authenticatedTsigKeyName">
    ///     The name of the TSIG key <see cref="Authentication.ClusterTsigAuthenticationHandler" />
    ///     already verified this request against -- a real, in-assembly reference, since this
    ///     class and that handler are both part of <c>Ninlil.Dns.Server.Cluster</c>.
    /// </param>
    /// <param name="request">The joiner's own callback payload.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The peer registry row now on record for the joiner.</returns>
    /// <exception cref="ValidationException">Thrown when clustering is disabled, or the joiner claims this node's own identifier.</exception>
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

            // An idempotent retry: refresh this row's address/certificate/name, but leave its
            // own observed Status/heartbeat history exactly as it was.
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

            // A successful, authenticated callback is itself live proof this direction of the
            // channel works -- there is no reason to leave this row in Joining until the next
            // scheduled heartbeat probe confirms what this request already just demonstrated.
            Status = ClusterNodeStatusEnum.Healthy,
            LastHeartbeatAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await clusterNodeRepository.AddAsync(node, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return node;
    }
}
