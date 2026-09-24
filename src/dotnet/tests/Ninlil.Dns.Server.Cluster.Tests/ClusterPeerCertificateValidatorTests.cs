using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ninlil.Dns.Server.Cluster.Http;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests;

/// <summary>
///     This class covers <see cref="ClusterPeerCertificateValidator" /> against real, generated
///     self-signed certificates -- the exact shape a Ninlil cluster channel actually presents
///     (<c>TlsCertificateOptions</c>'s own default), not a mock.
/// </summary>
public class ClusterPeerCertificateValidatorTests
{
    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName = "CN=localhost")
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new(
            subjectName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5)
        );
    }

    [Fact]
    public void ComputeThumbprint_IsDeterministicForTheSameCertificate()
    {
        using X509Certificate2 certificate = CreateSelfSignedCertificate();

        string first = ClusterPeerCertificateValidator.ComputeThumbprint(certificate);
        string second = ClusterPeerCertificateValidator.ComputeThumbprint(certificate);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeThumbprint_IsLowercaseHexWithNoSeparators()
    {
        using X509Certificate2 certificate = CreateSelfSignedCertificate();

        string thumbprint = ClusterPeerCertificateValidator.ComputeThumbprint(certificate);

        Assert.Matches("^[0-9a-f]{64}$", thumbprint);
    }

    [Fact]
    public void ComputeThumbprint_DiffersForTwoDistinctCertificates()
    {
        using X509Certificate2 first = CreateSelfSignedCertificate("CN=node-a");
        using X509Certificate2 second = CreateSelfSignedCertificate("CN=node-b");

        Assert.NotEqual(
            ClusterPeerCertificateValidator.ComputeThumbprint(first),
            ClusterPeerCertificateValidator.ComputeThumbprint(second)
        );
    }

    [Fact]
    public void IsTrusted_WithAMatchingPin_ReturnsTrue()
    {
        using X509Certificate2 certificate = CreateSelfSignedCertificate();
        string pin = ClusterPeerCertificateValidator.ComputeThumbprint(certificate);

        Assert.True(ClusterPeerCertificateValidator.IsTrusted(certificate, pin));
    }

    [Fact]
    public void IsTrusted_IsCaseInsensitive()
    {
        using X509Certificate2 certificate = CreateSelfSignedCertificate();
        string pin = ClusterPeerCertificateValidator
            .ComputeThumbprint(certificate)
            .ToUpperInvariant();

        Assert.True(ClusterPeerCertificateValidator.IsTrusted(certificate, pin));
    }

    [Fact]
    public void IsTrusted_WithAMismatchedPin_ReturnsFalse()
    {
        using X509Certificate2 presented = CreateSelfSignedCertificate("CN=the-real-peer");
        using X509Certificate2 attacker = CreateSelfSignedCertificate("CN=an-impostor");
        string pinnedToTheRealPeer = ClusterPeerCertificateValidator.ComputeThumbprint(presented);

        // The whole point of pinning: a certificate presenting a different key entirely, even
        // one with a plausible-looking subject name, is rejected outright.
        Assert.False(ClusterPeerCertificateValidator.IsTrusted(attacker, pinnedToTheRealPeer));
    }

    [Fact]
    public void IsTrusted_WithANullCertificate_ReturnsFalse()
    {
        Assert.False(ClusterPeerCertificateValidator.IsTrusted(null, new string('a', 64)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTrusted_WithNoPin_ReturnsFalse(string blankPin)
    {
        using X509Certificate2 certificate = CreateSelfSignedCertificate();

        Assert.False(ClusterPeerCertificateValidator.IsTrusted(certificate, blankPin));
    }
}
