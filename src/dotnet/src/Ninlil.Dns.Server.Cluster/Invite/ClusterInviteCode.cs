// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Invite;

/// <summary>
///     This class is the payload a cluster invite code decodes to: everything a second node
///     needs to join the node that issued it, so the operator only copies one string across.
///     <see cref="ClusterInviteCodec" /> encodes and decodes it, and
///     <see cref="ClusterInviteService.CreateInviteAsync" /> creates it.
///     <para>
///         The payload carries the shared secret in plaintext. That fits the rule that a secret
///         is revealed once, in the create response, and never stored in plaintext: the invite
///         code is that one reveal, as <c>TsigKeyCreateResponse</c> is for a new TSIG key. The
///         joining node stores the secret encrypted with <c>TsigSecretProtector</c>, as the
///         inviter does, and the invite code itself is not stored.
///     </para>
/// </summary>
public class ClusterInviteCode
{
    /// <summary>
    ///     This property contains the payload version, so a different format is rejected instead
    ///     of misread. Only <c>1</c> is written and accepted.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    ///     This property contains the inviting node's <c>ClusterOptions.NodeId</c>.
    /// </summary>
    public Guid InviterNodeId { get; set; }

    /// <summary>
    ///     This property contains the host name or IP address at which the joining node reaches
    ///     the inviter's cluster channel.
    /// </summary>
    public string InviterHost { get; set; }

    /// <summary>
    ///     This property contains the TCP port at which the joining node reaches the inviter's
    ///     cluster channel. The decoder rejects 0.
    /// </summary>
    public int InviterPort { get; set; }

    /// <summary>
    ///     This property contains the name of the new TSIG key both nodes sign their
    ///     cluster-channel requests to each other with.
    /// </summary>
    public string TsigKeyName { get; set; }

    /// <summary>
    ///     This property contains that key's shared secret, base64-encoded, in plaintext (see the
    ///     class summary).
    /// </summary>
    public string TsigKeySecretBase64 { get; set; }

    /// <summary>
    ///     This property contains the SHA-256 thumbprint of the TLS certificate the inviter
    ///     presents, for the joining node to pin.
    /// </summary>
    public string InviterCertificateThumbprint { get; set; }

    /// <summary>
    ///     This property contains when the invite expires (see
    ///     <see cref="ClusterInviteService.DefaultValidity" />). Only the joining node checks it;
    ///     the inviter accepts the key whenever it is used.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
