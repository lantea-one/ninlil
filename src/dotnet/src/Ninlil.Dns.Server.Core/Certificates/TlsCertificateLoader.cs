using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.IO;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Certificates;

/// <summary>
///     This class turns a <see cref="TlsCertificateOptions" /> into the
///     <see cref="X509Certificate2" /> a TLS transport presents: the configured PFX file, or a
///     self-signed certificate generated once and saved. Every transport with the default
///     settings shares the same state file, so they present the same self-signed certificate.
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
    ///     This method generates a new self-signed ECDSA P-256 certificate for server
    ///     authentication, valid for two years. The first name is the subject CN; every name is a
    ///     subject alternative name.
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
    ///     This method saves a certificate with its private key as an unencrypted PFX through
    ///     <see cref="AtomicFile" />, which writes it atomically and, on non-Windows platforms,
    ///     limits the file to its owner from the moment it exists.
    /// </summary>
    private static void Persist(X509Certificate2 certificate, string path, ILogger logger)
    {
        try
        {
            AtomicFile.WriteAllBytes(
                path,
                certificate.Export(X509ContentType.Pfx),
                AtomicFilePermissions.OwnerOnly
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The in-memory certificate still works for this run; the next start just
            // generates a new one, so this is only a warning.
            logger.LogWarning(
                exception,
                "Failed to persist the self-signed TLS certificate to '{Path}'.",
                path
            );
        }
    }
}
