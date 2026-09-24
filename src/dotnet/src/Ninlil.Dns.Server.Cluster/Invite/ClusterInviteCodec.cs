using System.Text.Json;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Invite;

/// <summary>
///     This class encodes a <see cref="ClusterInviteCode" /> into a single opaque string an
///     operator copies from the inviting node's own Angular "Cluster" page into the joining
///     node's -- and decodes it back. The encoding itself (JSON, then base64url) carries no
///     security property of its own; the payload's own secret is protected purely by staying off
///     the wire except over this project's own TLS-protected admin REST API (JWT-authenticated,
///     served only over Kestrel's HTTPS listener) and by the operator's own out-of-band copy/paste
///     step, exactly like <c>TsigKeyCreateResponse</c>'s own plaintext secret already is.
/// </summary>
public static class ClusterInviteCodec
{
    /// <summary>
    ///     This constant contains a short, human-visible prefix every invite code starts with, so
    ///     an operator (or this project's own <c>ClusterController.Join</c> validation) can tell
    ///     an obviously-wrong paste (an empty field, a TSIG secret pasted into the wrong box) apart
    ///     from a real, if perhaps expired or otherwise invalid, invite code before ever
    ///     attempting to decode it.
    /// </summary>
    private const string Prefix = "ninlil-cluster-invite-1.";

    /// <summary>
    ///     This method encodes an invite payload into its opaque string form.
    /// </summary>
    /// <param name="invite">The payload to encode.</param>
    public static string Encode(ClusterInviteCode invite)
    {
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(invite);

        // How: ordinary base64's '+'/'/' characters and '=' padding are not safe to copy/paste
        // or embed in a URL/form field without escaping, so the standard base64 alphabet is
        // converted to the base64url variant (RFC 4648 §5: '+' -> '-', '/' -> '_') and its
        // padding is trimmed entirely -- TryDecode below reverses both transformations exactly.
        return Prefix
            + Convert.ToBase64String(jsonBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    ///     This method decodes an opaque invite code string back into its payload.
    /// </summary>
    /// <param name="inviteCode">The opaque string to decode.</param>
    /// <param name="invite">The decoded payload, or <see langword="null" /> on failure.</param>
    /// <param name="error">A human-readable explanation of the failure, or <see langword="null" /> on success.</param>
    /// <returns><see langword="true" /> if decoding succeeded.</returns>
    public static bool TryDecode(string inviteCode, out ClusterInviteCode invite, out string error)
    {
        invite = null;
        error = null;

        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            error = "The invite code is empty.";
            return false;
        }

        string trimmed = inviteCode.Trim();

        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "This does not look like a Ninlil cluster invite code.";
            return false;
        }

        string base64Url = trimmed[Prefix.Length..];
        string base64 = base64Url.Replace('-', '+').Replace('_', '/');

        // Restore the padding TrimEnd('=') removed during encoding.
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

        byte[] jsonBytes;
        try
        {
            jsonBytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            error = "The invite code is not validly encoded.";
            return false;
        }

        ClusterInviteCode decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<ClusterInviteCode>(jsonBytes);
        }
        catch (JsonException)
        {
            error = "The invite code's own payload is corrupt.";
            return false;
        }

        if (decoded is null)
        {
            error = "The invite code's own payload is empty.";
            return false;
        }

        if (decoded.Version != 1)
        {
            error =
                $"This invite code uses format version {decoded.Version}, which this server does not understand.";
            return false;
        }

        if (
            decoded.InviterNodeId == Guid.Empty
            || string.IsNullOrWhiteSpace(decoded.InviterHost)
            || decoded.InviterPort is <= 0 or > 65535
            || string.IsNullOrWhiteSpace(decoded.TsigKeyName)
            || string.IsNullOrWhiteSpace(decoded.TsigKeySecretBase64)
            || string.IsNullOrWhiteSpace(decoded.InviterCertificateThumbprint)
        )
        {
            error = "The invite code's own payload is missing required fields.";
            return false;
        }

        invite = decoded;
        return true;
    }
}
