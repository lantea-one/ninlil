using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Certificates;

/// <summary>
///     This class resolves a <see cref="TlsCertificateOptions" /> configuration into an actual
///     <see cref="X509Certificate2" /> a TLS-based transport can present to a connecting client
///     -- loading an operator-provided one, or generating (and durably persisting) a self-signed
///     one -- shared identically by every transport that needs a certificate at all, so DNS-over-
///     TLS and DNS-over-HTTPS present the same server identity by default when neither is
///     separately configured with its own certificate.
/// </summary>
public static class TlsCertificateLoader
{
    /// <summary>
    ///     This method loads or generates a certificate per <paramref name="options" />.
    /// </summary>
    /// <param name="options">Where to load an existing certificate from, or how to generate one.</param>
    /// <param name="logger">The logger this method records a newly generated certificate, or a load failure, to.</param>
    /// <returns>The resolved certificate, including its private key.</returns>
    public static X509Certificate2 LoadOrGenerate(TlsCertificateOptions options, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.PfxPath))
            return X509CertificateLoader.LoadPkcs12FromFile(options.PfxPath, options.PfxPassword);

        string statePath = options.SelfSignedCertificateStatePath;

        if (File.Exists(statePath))
        {
            try
            {
                return X509CertificateLoader.LoadPkcs12FromFile(statePath, "");
            }
            catch (CryptographicException exception)
            {
                logger.LogWarning(
                    exception,
                    "The self-signed certificate at '{Path}' could not be read; generating a new one.",
                    statePath
                );
            }
        }

        X509Certificate2 certificate = GenerateSelfSigned(
            options.SelfSignedSubjectAlternativeNames
        );
        Persist(certificate, statePath, logger);

        logger.LogInformation(
            "Generated a new self-signed TLS certificate (subject alternative names: {Names}), persisted to '{Path}'.",
            string.Join(", ", options.SelfSignedSubjectAlternativeNames),
            statePath
        );

        return certificate;
    }

    /// <summary>
    ///     This method generates a new self-signed certificate, valid for two years, suitable
    ///     for server (not client) TLS authentication.
    /// </summary>
    private static X509Certificate2 GenerateSelfSigned(
        IReadOnlyList<string> subjectAlternativeNames
    )
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new(
            $"CN={subjectAlternativeNames[0]}",
            key,
            HashAlgorithmName.SHA256
        );

        SubjectAlternativeNameBuilder sanBuilder = new();

        foreach (string name in subjectAlternativeNames)
        {
            if (IPAddress.TryParse(name, out IPAddress address))
                sanBuilder.AddIpAddress(address);
            else
                sanBuilder.AddDnsName(name);
        }

        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false)
        );
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false
            )
        );
        request.CertificateExtensions.Add(
            // Server Authentication (1.3.6.1.5.5.7.3.1), per RFC 5280.
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false)
        );

        DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);

        return request.CreateSelfSigned(notBefore, notBefore.AddYears(2));
    }

    /// <summary>
    ///     This method persists a certificate (including its private key) to disk, restricting
    ///     its file permissions to this process's own owner wherever the underlying platform
    ///     supports doing so.
    /// </summary>
    private static void Persist(X509Certificate2 certificate, string path, ILogger logger)
    {
        try
        {
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException exception)
        {
            // A failed write leaves the in-memory certificate (already returned to the caller)
            // usable for the rest of this process's lifetime; only a *future* restart would
            // regenerate a new one instead of reusing this one, which is an availability/UX
            // concern, not a security or correctness one -- a warning, not fatal.
            logger.LogWarning(
                exception,
                "Failed to persist the self-signed TLS certificate to '{Path}'.",
                path
            );
        }
    }
}
