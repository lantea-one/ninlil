// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a shared TSIG (RFC 8945) key used to authenticate zone transfers and
///     dynamic updates. An operator creates one through the admin console or API for each
///     trusted secondary server or update client.
///     <para>
///         The cluster channel (<c>ClusterTsigAuthenticationHandler</c>) uses TSIG keys too, but
///         only keys clustering created itself: an invite's key (<see cref="ClusterInviteExpiresAtUtc" />)
///         until it is used, then the key of one peer (<see cref="ClusterNodeId" />). A key an
///         operator creates has neither and never authenticates a peer.
///     </para>
/// </summary>
public class TsigKeyEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the key's name, in domain-name presentation form (it need not
    ///     resolve). It is matched against the key name in a signed message.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the MAC algorithm's name, in domain-name presentation form.
    ///     It is always <c>hmac-sha256.</c>, since Ninlil implements only HMAC-SHA256, but it is a
    ///     column so another algorithm can be added without a schema change.
    /// </summary>
    public string AlgorithmName { get; set; }

    /// <summary>
    ///     This property contains the shared secret, encrypted at rest. Unlike a password or API
    ///     token (see <see cref="ApiTokenEntity.TokenHash" />) it cannot be a one-way hash, because
    ///     computing a MAC needs the secret itself.
    /// </summary>
    public string SecretEncrypted { get; set; }

    /// <summary>
    ///     This property indicates whether the key is disabled. A disabled key is kept but treated as
    ///     unknown: signing or verifying with it fails with a TSIG BADKEY error.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about the key, such as which secondary
    ///     server or update client it was issued to.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property contains the <see cref="EntityBase.Id" /> of the
    ///     <see cref="ClusterNodeEntity" /> this key belongs to, or <see langword="null" /> for a
    ///     key that is not a peer's cluster channel key. A signed cluster-channel request is
    ///     accepted only from a key linked to a known peer, so an operator's zone-transfer key
    ///     can never act as a cluster node. Only <c>ClusterJoinService</c> sets it; the TSIG key
    ///     API does not expose it, and a key re-created under the same name starts unlinked.
    /// </summary>
    public Guid? ClusterNodeId { get; set; }

    /// <summary>
    ///     This property contains when the cluster invite this key was generated for expires, or
    ///     <see langword="null" /> if the key is not an unused invite key. Until then the key may
    ///     sign only the join callback (<c>ClusterChannelController.JoinComplete</c>); a successful
    ///     join clears it and sets <see cref="ClusterNodeId" />, and the heartbeat service removes
    ///     the key once it has expired unused.
    /// </summary>
    public DateTimeOffset? ClusterInviteExpiresAtUtc { get; set; }
}
