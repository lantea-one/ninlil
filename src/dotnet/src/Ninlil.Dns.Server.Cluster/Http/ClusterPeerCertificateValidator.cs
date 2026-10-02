using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Http;

/// <summary>
///     This class implements certificate pinning for the cluster channel. Chain validation is
///     not used, because most installations use the default self-signed certificate
///     (<c>TlsCertificateOptions</c>). Instead the SHA-256 thumbprint of the peer's leaf
///     certificate is compared with the one learned in the join handshake (carried in the invite
///     or the join callback) and stored in <c>ClusterNodeEntity.TlsCertificateThumbprint</c>,
///     much like a known SSH host key. When a peer's certificate is renewed, the peer announces
///     the new thumbprint in its next signed heartbeat and the stored pin is updated
///     (<c>ClusterHeartbeatReceiver</c>); until then, probes of that peer fail the check.
/// </summary>
public static class ClusterPeerCertificateValidator
{
    /// <summary>
    ///     This method computes the SHA-256 thumbprint of a certificate, as lowercase hexadecimal
    ///     with no separators, the form stored in <c>ClusterNodeEntity.TlsCertificateThumbprint</c>.
    /// </summary>
    /// <param name="certificate">The certificate to compute the thumbprint of.</param>
    public static string ComputeThumbprint(X509Certificate2 certificate) =>
        Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256));

    /// <summary>
    ///     This method decides whether a certificate presented during a cluster-channel TLS
    ///     handshake should be trusted, for use as an
    ///     <see cref="System.Net.Http.HttpClientHandler.ServerCertificateCustomValidationCallback" />.
    ///     <c>sslPolicyErrors</c> is ignored: an untrusted chain, a self-signed certificate, an
    ///     expired one or a name mismatch is accepted as long as the thumbprint matches the pin
    ///     (compared ignoring case).
    /// </summary>
    /// <param name="presentedCertificate">The certificate the peer presented.</param>
    /// <param name="pinnedThumbprintHex">
    ///     The known-good thumbprint to compare against, from
    ///     <c>ClusterNodeEntity.TlsCertificateThumbprint</c>.
    /// </param>
    /// <returns><see langword="true" /> if the presented certificate matches the pin.</returns>
    public static bool IsTrusted(X509Certificate2 presentedCertificate, string pinnedThumbprintHex)
    {
        if (presentedCertificate is null || string.IsNullOrWhiteSpace(pinnedThumbprintHex))
            return false;

        string actualThumbprint = ComputeThumbprint(presentedCertificate);
        return string.Equals(
            actualThumbprint,
            pinnedThumbprintHex,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
