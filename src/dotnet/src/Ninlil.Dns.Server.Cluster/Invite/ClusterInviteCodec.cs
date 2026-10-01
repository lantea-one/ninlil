using System.Text.Json;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Invite;

/// <summary>
///     This class encodes a <see cref="ClusterInviteCode" /> into the string an operator copies
///     from the inviting node's Cluster page to the joining node's, and decodes it again. The
///     encoding (JSON, then base64url) protects nothing: the secret in it is protected only by
///     travelling over the authenticated HTTPS admin API and by the operator's copy and paste,
///     like the plaintext secret in <c>TsigKeyCreateResponse</c>.
/// </summary>
public static class ClusterInviteCodec
{
    /// <summary>
    ///     This constant contains the readable prefix every invite code starts with, so an obvious
    ///     wrong paste (for example a TSIG secret) is reported as such before decoding.
    /// </summary>
    private const string Prefix = "ninlil-cluster-invite-1.";

    /// <summary>
    ///     This method encodes an invite payload into its opaque string form.
    /// </summary>
    /// <param name="invite">The payload to encode.</param>
    public static string Encode(ClusterInviteCode invite)
    {
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(invite);

        // Base64url (RFC 4648 §5: '+' becomes '-', '/' becomes '_') without '=' padding, so the
        // code survives copy and paste and form fields unescaped. TryDecode reverses both.
        return Prefix
            + Convert.ToBase64String(jsonBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    ///     This method decodes an invite code and checks the version and required fields
    ///     (a non-empty node id, host, key name, secret and thumbprint, and a port from 1 to
    ///     65535). It does not check expiry; <c>ClusterJoinService.JoinAsync</c> does.
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
