using System.Security.Cryptography;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Authentication;

/// <summary>
///     This class computes and parses the HMAC-SHA256 signature every cluster-channel HTTP
///     request carries in its own <see cref="ClusterTsigAuthenticationDefaults.SignatureHeaderName" />
///     header. It reuses the exact same pre-shared-key storage, encryption
///     (<c>TsigSecretProtector</c>), and MAC algorithm (HMAC-SHA256) as real RFC 8945 TSIG --
///     the decision record for Phase 9-6a's node-to-node channel calls for "reusing the existing
///     <c>Ninlil.Dns.Server.Tsig</c> key infrastructure for application-layer authentication,
///     exactly like zone transfer already does." It deliberately does <b>not</b> reuse
///     <c>ITsigService</c>/<c>TsigCodec</c> directly, though: those compute a MAC over RFC 8945's
///     own DNS-message-specific "TSIG Variables" wire encoding, a format built for a binary DNS
///     message, not a JSON HTTP request. This class instead signs a simple, HTTP-appropriate
///     canonical string -- the request method, path, a Unix timestamp, and the raw request body
///     -- which is the same key material and algorithm applied to a shape that actually fits an
///     HTTP call.
/// </summary>
public static class ClusterTsigRequestSigner
{
    /// <summary>
    ///     This constant contains how many seconds of clock skew between the signing and
    ///     verifying node are tolerated before a signature is rejected as stale -- matching RFC
    ///     8945's own recommended TSIG fudge default (<c>TsigServiceOptions.DefaultFudgeSeconds</c>)
    ///     exactly, for the same reasoning: long enough to absorb ordinary clock drift between
    ///     two independent hosts, short enough that a captured, signed request cannot be replayed
    ///     indefinitely.
    /// </summary>
    public const int ClockSkewToleranceSeconds = 300;

    /// <summary>
    ///     This method computes the HMAC-SHA256 MAC for one cluster-channel request.
    /// </summary>
    /// <param name="secret">The shared secret to sign with, already decrypted.</param>
    /// <param name="httpMethod">The request's own HTTP method (for example, <c>"POST"</c>).</param>
    /// <param name="path">The request's own absolute path (for example, <c>"/api/cluster/channel/heartbeat"</c>).</param>
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
        // What/how: the canonical string this class's own doc comment describes is built as a
        // newline-separated method/path/timestamp header, then the raw body bytes are appended
        // after it -- binding all four pieces into one MAC means an attacker who captures a
        // valid, signed request can never replay it against a different path, method, or body
        // (each would change the canonical input and therefore invalidate the MAC), nor merely
        // adjust its timestamp to extend its validity window.
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
    ///     This method parses a cluster-channel request's own signature header value back into
    ///     its three parts.
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

        // The key name itself is a DNS name in presentation form, which may legally contain a
        // colon-free but otherwise arbitrary label set; splitting into at most three parts (key
        // name, timestamp, MAC) rather than on every colon keeps this parse correct even so.
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
