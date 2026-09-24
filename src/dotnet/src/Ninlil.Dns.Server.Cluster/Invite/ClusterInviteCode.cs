// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Invite;

/// <summary>
///     This class is the plaintext payload a Ninlil cluster invite code decodes to -- everything
///     a second node needs to complete a join against the node that issued it, with no further
///     round trip or operator coordination beyond copying this one opaque string across. See
///     <see cref="ClusterInviteCodec" /> for how this is encoded into (and parsed back out of)
///     that opaque string, and <c>ClusterInviteService.CreateInviteAsync</c> for how one is
///     produced.
///     <para>
///         Carrying the shared secret in plaintext inside this payload is a deliberate,
///         necessary exception to this project's usual "reveal exactly once, from the
///         create/rotate response, never persisted in plaintext anywhere else" secret policy: an
///         invite code <em>is</em> that one-time reveal, exactly like <c>TsigKeyCreateResponse</c>
///         itself carries a freshly generated TSIG key's own plaintext secret. Once decoded and
///         consumed by <c>ClusterJoinService.JoinAsync</c>, the secret is re-encrypted at rest
///         (via <c>TsigSecretProtector</c>) on the joining node exactly the same way it already
///         was on the inviting node -- this invite code itself is never persisted anywhere, only
///         ever held in memory for the length of one join attempt.
///     </para>
/// </summary>
public class ClusterInviteCode
{
    /// <summary>
    ///     This property contains the invite payload's own schema version, so a future format
    ///     change can be detected and rejected cleanly rather than silently misparsed. Always
    ///     <c>1</c> today.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    ///     This property contains the inviting node's own stable <c>ClusterOptions.NodeId</c>.
    /// </summary>
    public Guid InviterNodeId { get; set; }

    /// <summary>
    ///     This property contains the hostname or IP address the joining node should reach the
    ///     inviter's own cluster channel at.
    /// </summary>
    public string InviterHost { get; set; }

    /// <summary>
    ///     This property contains the TCP port the joining node should reach the inviter's own
    ///     cluster channel at.
    /// </summary>
    public int InviterPort { get; set; }

    /// <summary>
    ///     This property contains the name of the freshly generated TSIG key both nodes will
    ///     authenticate their own cluster-channel requests to each other with.
    /// </summary>
    public string TsigKeyName { get; set; }

    /// <summary>
    ///     This property contains that key's own freshly generated shared secret, base64-encoded
    ///     -- see this class's own doc comment for why plaintext here is a deliberate, accepted
    ///     exception to this project's usual secret-handling policy.
    /// </summary>
    public string TsigKeySecretBase64 { get; set; }

    /// <summary>
    ///     This property contains the SHA-256 thumbprint of the TLS certificate the inviter
    ///     presents on its own cluster channel, for the joining node to pin.
    /// </summary>
    public string InviterCertificateThumbprint { get; set; }

    /// <summary>
    ///     This property contains the UTC moment after which this invite can no longer be
    ///     consumed -- see <c>ClusterInviteService.DefaultValidity</c> for the default window.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
