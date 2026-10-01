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
///     This class creates cluster invite codes, the inviting half of the join handshake (see
///     <see cref="ClusterInviteCode" />, and <see cref="Join.ClusterJoinService" /> for the joining
///     half). It generates and stores a new TSIG key before handing out the code, because the
///     join callback (<c>ClusterChannelController.JoinComplete</c>) is authenticated with that
///     key. No "pending invite" record is kept: holding the code, and so the key's secret, is the
///     authorization to join. An invite that is never used leaves its key stored and enabled;
///     nothing removes it, and since the inviter does not check the invite's expiry, the key
///     still lets its holder join after the invite has expired.
/// </summary>
/// <param name="tsigKeyRepository">The repository the invite's new key is stored through.</param>
/// <param name="unitOfWork">The unit of work the new key is committed through.</param>
/// <param name="secretProtector">The protector used to encrypt the new key's secret at rest.</param>
/// <param name="clusterOptions">This node's cluster identity and address.</param>
/// <param name="certificateProvider">The provider of this node's TLS certificate, whose thumbprint goes into the invite.</param>
public class ClusterInviteService(
    ITsigKeyRepository tsigKeyRepository,
    IUnitOfWork unitOfWork,
    TsigSecretProtector secretProtector,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ReloadableTlsCertificateProvider certificateProvider
)
{
    /// <summary>
    ///     This field contains how long a new invite is valid: long enough to copy it from this
    ///     node's Cluster page to the joining node's, even across browser sessions, and short
    ///     enough that a forgotten invite stops being accepted by a joining node soon after.
    /// </summary>
    public static readonly TimeSpan DefaultValidity = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     This constant contains the length, in bytes, of the new key's secret: 256 bits, the
    ///     HMAC-SHA256 output size, as <c>TsigKeysController</c> uses for a generated key.
    /// </summary>
    private const int GeneratedSecretLengthBytes = 32;

    /// <summary>
    ///     This field contains the HMAC-SHA256 algorithm name without the trailing dot, as stored
    ///     on TSIG keys.
    /// </summary>
    private static readonly string HmacSha256AlgorithmName = DnsTsigOptions
        .HmacSha256AlgorithmName.ToString()
        .TrimEnd('.');

    /// <summary>
    ///     This method creates an invite: it stores a new TSIG key and encodes it with this node's
    ///     identifier, advertised address and port, and certificate thumbprint into an invite code.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The invite code for the joining node's operator.</returns>
    /// <exception cref="ValidationException">
    ///     Thrown when clustering is disabled or <c>ClusterOptions.AdvertisedHost</c> is not set.
    ///     <c>ClusterOptions.AdvertisedPort</c> is not checked here, but an invite with port 0 is
    ///     rejected when the joining node decodes it.
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

        // A new random secret and unique key name per invite, so each join has its own key that
        // can be disabled or traced on its own.
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

        // The key is stored first, because the joiner's callback is authenticated against it.
        // The invite then carries this node's id, address and port, the plaintext secret the
        // joiner signs its callback with, and the certificate thumbprint the joiner pins.
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
