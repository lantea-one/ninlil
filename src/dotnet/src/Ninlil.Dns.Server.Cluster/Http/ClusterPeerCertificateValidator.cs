using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Http;

/// <summary>
///     This class implements certificate pinning for the cluster channel: rather than relying on
///     ordinary certificate-chain trust (which most Ninlil deployments cannot satisfy in the
///     first place -- <c>TlsCertificateOptions</c>'s own default is a self-signed certificate
///     with no real chain to validate), a peer's own presented leaf certificate is compared
///     directly, byte-for-byte, against the SHA-256 thumbprint learned once during the join
///     handshake and stored in <c>ClusterNodeEntity.TlsCertificateThumbprint</c>. This is the
///     same trust model an operator manually adding a known SSH host key, or pinning a known TLS
///     certificate, already represents: trust-on-first-use, verified thereafter.
/// </summary>
public static class ClusterPeerCertificateValidator
{
    /// <summary>
    ///     This method computes the SHA-256 thumbprint of a certificate, as lowercase hexadecimal
    ///     with no separators -- the canonical text format every cluster-channel component
    ///     agrees on, including <c>ClusterNodeEntity.TlsCertificateThumbprint</c> itself.
    /// </summary>
    /// <param name="certificate">The certificate to compute the thumbprint of.</param>
    public static string ComputeThumbprint(X509Certificate2 certificate) =>
        Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256));

    /// <summary>
    ///     This method decides whether a certificate presented during a cluster-channel TLS
    ///     handshake should be trusted, for use as an
    ///     <see cref="System.Net.Http.HttpClientHandler.ServerCertificateCustomValidationCallback" />.
    ///     Deliberately ignores <c>sslPolicyErrors</c> entirely -- an untrusted chain or an
    ///     expired/self-signed certificate is exactly the expected, normal case here; the pinned
    ///     thumbprint comparison is the only trust decision that matters.
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
