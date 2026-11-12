using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Define our namespace.
namespace Ninlil.Web.Tests.Fakes;

/// <summary>
///     This class creates the certificates and key ring directories the Data Protection key ring
///     tests need, in memory and in a caller-owned temporary directory. Nothing is read from the
///     machine's certificate store and no fixed path is used.
/// </summary>
public static class KeyRingTestCertificates
{
    /// <summary>
    ///     This method writes a self-signed RSA certificate with its private key to a PKCS#12
    ///     file.
    /// </summary>
    /// <param name="path">Where the file is written.</param>
    /// <param name="password">The file's password, or <see langword="null" /> for none.</param>
    public static void WriteRsaPfx(string path, string password)
    {
        using RSA rsa = RSA.Create(2048);
        Write(
            path,
            password,
            Sign(
                new CertificateRequest(
                    "CN=ninlil-keyring-test",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                )
            ),
            withPrivateKey: true
        );
    }

    /// <summary>
    ///     This method writes a self-signed RSA certificate without its private key to a PKCS#12
    ///     file, as an export of the public part only would.
    /// </summary>
    /// <param name="path">Where the file is written.</param>
    public static void WritePublicOnlyPfx(string path)
    {
        using RSA rsa = RSA.Create(2048);
        Write(
            path,
            null,
            Sign(
                new CertificateRequest(
                    "CN=ninlil-keyring-test",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                )
            ),
            withPrivateKey: false
        );
    }

    /// <summary>
    ///     This method writes a self-signed ECDSA certificate with its private key to a PKCS#12
    ///     file, which the key ring cannot use.
    /// </summary>
    /// <param name="path">Where the file is written.</param>
    public static void WriteEcdsaPfx(string path)
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Write(
            path,
            null,
            Sign(new CertificateRequest("CN=ninlil-keyring-test", ecdsa, HashAlgorithmName.SHA256)),
            withPrivateKey: true
        );
    }

    /// <summary>
    ///     This method creates an empty directory under the system temporary directory.
    /// </summary>
    /// <returns>The new directory's path.</returns>
    public static string NewDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ninlil-keyring-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    ///     This method self-signs a request for one day.
    /// </summary>
    private static X509Certificate2 Sign(CertificateRequest request) =>
        request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(1)
        );

    /// <summary>
    ///     This method exports a certificate, with or without its private key, to a PKCS#12 file
    ///     and releases it.
    /// </summary>
    private static void Write(
        string path,
        string password,
        X509Certificate2 certificate,
        bool withPrivateKey
    )
    {
        using (certificate)
        {
            if (withPrivateKey)
            {
                File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
                return;
            }

            using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(
                certificate.RawData
            );
            File.WriteAllBytes(path, publicOnly.Export(X509ContentType.Pfx));
        }
    }
}
