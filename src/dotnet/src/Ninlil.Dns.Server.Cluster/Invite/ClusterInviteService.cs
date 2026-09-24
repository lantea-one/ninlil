using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Http;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Invite;

/// <summary>
///     This class creates a cluster invite code: the inviting-side half of Phase 9-6a's join
///     handshake (see <see cref="ClusterInviteCode" />'s own doc comment for the full design, and
///     <c>ClusterJoinService</c> for the joining-side half). Creating an invite generates and
///     persists a brand-new, dedicated TSIG key up front, before the invite code is ever handed to
///     an operator -- <c>ClusterChannelController.JoinComplete</c> needs that key to already exist
///     locally the moment a real join callback arrives, and there is no separate "pending invite"
///     record kept anywhere: possessing the invite code (and therefore the key's own shared
///     secret it carries) <em>is</em> this project's whole proof of authorization to join, exactly
///     the way possessing a freshly generated TSIG key's own one-time-revealed secret already
///     authorizes a zone transfer client. An invite that is never consumed before it expires
///     leaves its own now-useless TSIG key on record, harmlessly unused -- a known, accepted
///     loose end, not addressed by an automatic cleanup sweep here.
/// </summary>
/// <param name="tsigKeyRepository">The repository this service persists the invite's own fresh key through.</param>
/// <param name="unitOfWork">The unit of work the new key is committed through.</param>
/// <param name="secretProtector">The protector used to encrypt the new key's own shared secret at rest.</param>
/// <param name="clusterOptions">This node's own cluster identity/address.</param>
/// <param name="certificateProvider">The provider this node's own cluster channel TLS certificate is read from.</param>
public class ClusterInviteService(
    ITsigKeyRepository tsigKeyRepository,
    IUnitOfWork unitOfWork,
    TsigSecretProtector secretProtector,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ReloadableTlsCertificateProvider certificateProvider
)
{
    /// <summary>
    ///     This constant contains how long a freshly created invite stays valid for. Long enough
    ///     for an operator to copy the code out of this node's own Angular "Cluster" page and
    ///     paste it into the joining node's, even across two entirely separate browser sessions on
    ///     different machines; short enough that a leaked or simply forgotten, never-consumed
    ///     invite cannot be used long after the operator moved on.
    /// </summary>
    public static readonly TimeSpan DefaultValidity = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     This constant contains the length, in bytes, of the invite's own freshly generated TSIG
    ///     shared secret -- matching HMAC-SHA256's own 256-bit block size, the only algorithm
    ///     Ninlil implements (the same constant <c>TsigKeysController</c> itself uses for an
    ///     operator-generated key).
    /// </summary>
    private const int GeneratedSecretLengthBytes = 32;

    /// <summary>
    ///     This property contains the canonical, dot-stripped presentation form of the only MAC
    ///     algorithm Ninlil implements.
    /// </summary>
    private static readonly string HmacSha256AlgorithmName = DnsTsigOptions
        .HmacSha256AlgorithmName.ToString()
        .TrimEnd('.');

    /// <summary>
    ///     This method creates a new invite: a fresh TSIG key, persisted immediately, and this
    ///     node's own current identity/address/certificate, encoded into one opaque invite code.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The opaque invite code to hand to the joining node's own operator.</returns>
    /// <exception cref="ValidationException">
    ///     Thrown when clustering is disabled, or this node's own <c>ClusterOptions.AdvertisedHost</c>
    ///     has not been configured -- an invite naming an unreachable or unset address would be
    ///     useless to whoever receives it.
    /// </exception>
    public async Task<string> CreateInviteAsync(CancellationToken stoppingToken = default)
    {
        ClusterOptions options = clusterOptions.CurrentValue;

        if (!options.Enable)
            throw new ValidationException(
                "Clustering is not enabled on this node -- set 'Cluster:Enable' before inviting a peer."
            );

        if (string.IsNullOrWhiteSpace(options.AdvertisedHost))
            throw new ValidationException(
                "This node's own 'Cluster:AdvertisedHost' has not been configured -- a peer would have no way to reach it."
            );

        // What/why/how: a brand-new, cryptographically random secret and a globally unique key
        // name are generated fresh for every invite, rather than reusing an existing key -- the
        // invite code itself is this project's whole proof of authorization to join (per this
        // class's own doc comment), so each invite must carry its own dedicated, single-purpose
        // key that can later be individually disabled or traced back to exactly one join attempt.
        byte[] secretBytes = RandomNumberGenerator.GetBytes(GeneratedSecretLengthBytes);
        string keyName = $"cluster-{Guid.NewGuid():N}";

        TsigKeyEntity key = new()
        {
            Id = Guid.NewGuid(),
            Name = keyName,
            AlgorithmName = HmacSha256AlgorithmName,
            SecretEncrypted = secretProtector.Protect(secretBytes),
            IsDisabled = false,
            Comments =
                $"Cluster channel key generated for an invite issued {DateTimeOffset.UtcNow:O}.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await tsigKeyRepository.AddAsync(key, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // What/why/how: the fresh key is persisted locally before the invite payload is even
        // built, since the joining node's own callback (ClusterJoinService.HandleJoinCompleteAsync)
        // must find this exact key already on record the moment it arrives. The invite payload
        // itself then bundles this node's own reachable identity (address/port/node id), the raw
        // (not yet encrypted) secret the joiner needs to sign its own callback with, and this
        // node's own TLS certificate thumbprint the joiner will pin against -- everything the
        // other side of the handshake needs, in one self-contained, time-limited package.
        ClusterInviteCode invite = new()
        {
            InviterNodeId = options.NodeId,
            InviterHost = options.AdvertisedHost,
            InviterPort = options.AdvertisedPort,
            TsigKeyName = keyName,
            TsigKeySecretBase64 = Convert.ToBase64String(secretBytes),
            InviterCertificateThumbprint = ClusterPeerCertificateValidator.ComputeThumbprint(
                certificateProvider.Current
            ),
            ExpiresAtUtc = DateTimeOffset.UtcNow + DefaultValidity,
        };

        return ClusterInviteCodec.Encode(invite);
    }
}
