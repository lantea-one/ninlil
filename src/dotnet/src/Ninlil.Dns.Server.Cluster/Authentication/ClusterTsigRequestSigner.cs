using System.Security.Cryptography;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Authentication;

/// <summary>
///     This class computes and parses the HMAC-SHA256 signature every cluster-channel HTTP
///     request carries in the <see cref="ClusterTsigAuthenticationDefaults.SignatureHeaderName" />
///     header. It uses the TSIG key store and its encryption (<c>TsigSecretProtector</c>), the
///     same keys zone transfers use, but not the RFC 8945 message format: <c>ITsigService</c>
///     and <c>TsigCodec</c> sign DNS-message "TSIG Variables", which do not fit an HTTP request.
///     Here the MAC covers the method, path, a Unix timestamp and the raw body.
///     <see cref="Http.ClusterPeerHttpClientFactory" /> signs and
///     <see cref="ClusterTsigAuthenticationHandler" /> verifies.
/// </summary>
public static class ClusterTsigRequestSigner
{
    /// <summary>
    ///     This constant contains how many seconds of clock skew between the signing and
    ///     verifying node are allowed before a signature is rejected: 300, the RFC 8945 TSIG fudge
    ///     recommendation. That absorbs ordinary clock drift and limits replay of a captured
    ///     request to that window; there is no nonce, so a replay inside it is accepted.
    /// </summary>
    public const int ClockSkewToleranceSeconds = 300;

    /// <summary>
    ///     This method computes the HMAC-SHA256 MAC for one cluster-channel request.
    /// </summary>
    /// <param name="secret">The shared secret to sign with, already decrypted.</param>
    /// <param name="httpMethod">The request's HTTP method (for example, <c>"POST"</c>).</param>
    /// <param name="path">The request's absolute path (for example, <c>"/api/cluster/channel/heartbeat"</c>).</param>
    /// <param name="unixTimestampSeconds">The Unix timestamp the request is being signed at.</param>
    /// <param name="bodyBytes">The exact raw bytes of the request body.</param>
    /// <returns>The computed MAC.</returns>
    public static byte[] ComputeMac(
        byte[] secret,
        string httpMethod,
        string path,
        long unixTimestampSeconds,
        byte[] bodyBytes
    )
    {
        // Method, path and timestamp, each followed by a newline, then the raw body. Covering all
        // four means a captured request cannot be reused with another method, path or body, or
        // with a later timestamp.
        byte[] canonicalPrefix = Encoding.UTF8.GetBytes(
            $"{httpMethod.ToUpperInvariant()}\n{path}\n{unixTimestampSeconds}\n"
        );
        byte[] macInput = [.. canonicalPrefix, .. bodyBytes];

        return HMACSHA256.HashData(secret, macInput);
    }

    /// <summary>
    ///     This method formats a computed MAC into the header value a cluster-channel request
    ///     carries.
    /// </summary>
    /// <param name="tsigKeyName">The name of the key the MAC was computed with.</param>
    /// <param name="unixTimestampSeconds">The Unix timestamp the request was signed at.</param>
    /// <param name="mac">The computed MAC, from <see cref="ComputeMac" />.</param>
    public static string FormatHeaderValue(
        string tsigKeyName,
        long unixTimestampSeconds,
        byte[] mac
    ) => $"{tsigKeyName}:{unixTimestampSeconds}:{Convert.ToBase64String(mac)}";

    /// <summary>
    ///     This method parses a signature header value into its three parts.
    /// </summary>
    /// <param name="headerValue">The raw header value to parse.</param>
    /// <param name="tsigKeyName">The parsed key name, or <see langword="null" /> on failure.</param>
    /// <param name="unixTimestampSeconds">The parsed timestamp, or <c>0</c> on failure.</param>
    /// <param name="mac">The parsed MAC bytes, or <see langword="null" /> on failure.</param>
    /// <returns><see langword="true" /> if every part parsed successfully.</returns>
    public static bool TryParseHeaderValue(
        string headerValue,
        out string tsigKeyName,
        out long unixTimestampSeconds,
        out byte[] mac
    )
    {
        tsigKeyName = null;
        unixTimestampSeconds = 0;
        mac = null;

        if (string.IsNullOrWhiteSpace(headerValue))
            return false;

        // Split into at most three parts, so the base64 MAC (which has no colon) is the last.
        // A key name containing a colon would still break the parse.
        string[] parts = headerValue.Split(':', 3);
        if (parts.Length != 3)
            return false;

        if (string.IsNullOrWhiteSpace(parts[0]))
            return false;

        if (!long.TryParse(parts[1], out long parsedTimestamp))
            return false;

        try
        {
            mac = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (mac.Length == 0)
            return false;

        tsigKeyName = parts[0];
        unixTimestampSeconds = parsedTimestamp;
        return true;
    }
}
