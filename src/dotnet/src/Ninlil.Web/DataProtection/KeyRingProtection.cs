using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.DataProtection;

/// <summary>
///     This class holds how the Data Protection key ring is encrypted at rest: the mode, and the
///     certificates that protect and unprotect it. <c>Program.cs</c> builds it from
///     <see cref="DataProtectionKeyRingOptions" /> before the key ring is configured, calls
///     <see cref="Apply" />, and registers the instance so <see cref="KeyRingRestoreCheck" />
///     reads keys the same way the running key ring does.
///     <para>
///         <b>Order (owner decision R2-KEYRING).</b> A configured certificate wins on every
///         platform, Windows included. With none, Windows uses DPAPI and other platforms leave new
///         entries unencrypted. <b>A bad certificate setting stops the start</b> (see
///         <see cref="Resolve" />) instead of falling back, because a silent fallback would write
///         new keys unprotected, or under a different protection than the operator asked for.
///     </para>
///     <para>
///         <b>DPAPI scope.</b> The key ring is protected to the current account, not the machine.
///         Machine scope lets every local account decrypt the ring, which defeats the purpose;
///         the account is the same across upgrades of the same service, so the ring stays
///         readable. The price is that a different account (or machine) cannot read it; an
///         operator who needs that configures a certificate.
///     </para>
///     <para>
///         <b>Reading.</b> Data Protection reads each key file by the way that file was written,
///         so unencrypted and DPAPI keys keep decrypting after a certificate is configured (DPAPI
///         only on the same machine and account).
///     </para>
/// </summary>
public sealed class KeyRingProtection : IDisposable
{
    private readonly X509Certificate2 _current;
    private readonly X509Certificate2 _previous;

    private KeyRingProtection(
        KeyRingProtectionMode mode,
        X509Certificate2 current,
        X509Certificate2 previous
    )
    {
        Mode = mode;
        _current = current;
        _previous = previous;
    }

    /// <summary>
    ///     This property contains how new key ring entries are encrypted.
    /// </summary>
    public KeyRingProtectionMode Mode { get; }

    /// <summary>
    ///     This method reads the settings and loads their certificates.
    /// </summary>
    /// <param name="options">The bound <c>DataProtection</c> settings.</param>
    /// <param name="isWindows">Whether the platform is Windows, which decides between DPAPI and no encryption when no certificate is configured.</param>
    /// <returns>The protection to apply to the key ring.</returns>
    /// <exception cref="DatabaseStartupRefusedException">
    ///     A path is missing, a file cannot be opened (a wrong password is the usual reason), a
    ///     certificate has no private key or no RSA key, or a previous certificate is set without
    ///     a current one. The message names the setting.
    /// </exception>
    public static KeyRingProtection Resolve(DataProtectionKeyRingOptions options, bool isWindows)
    {
        bool hasCurrent = !string.IsNullOrWhiteSpace(options.CertificatePath);
        bool hasPrevious = !string.IsNullOrWhiteSpace(options.PreviousCertificatePath);

        if (!hasCurrent && hasPrevious)
            throw new DatabaseStartupRefusedException(
                "DataProtection:PreviousCertificatePath (NINLIL_DATA_PROTECTION_PREVIOUS_CERTIFICATE_PATH) "
                    + "is set but DataProtection:CertificatePath (NINLIL_DATA_PROTECTION_CERTIFICATE_PATH) is empty. "
                    + "The previous certificate only keeps older key ring entries readable while a current "
                    + "certificate encrypts new ones. Set DataProtection:CertificatePath or remove the previous setting."
            );

        if (!hasCurrent)
            return new KeyRingProtection(
                isWindows ? KeyRingProtectionMode.Dpapi : KeyRingProtectionMode.None,
                null,
                null
            );

        X509Certificate2 current = Load(
            options.CertificatePath,
            options.CertificatePassword,
            "DataProtection:CertificatePath",
            "DataProtection:CertificatePassword"
        );

        X509Certificate2 previous = null;
        if (hasPrevious)
        {
            try
            {
                previous = Load(
                    options.PreviousCertificatePath,
                    options.PreviousCertificatePassword,
                    "DataProtection:PreviousCertificatePath",
                    "DataProtection:PreviousCertificatePassword"
                );
            }
            catch
            {
                current.Dispose();
                throw;
            }
        }

        return new KeyRingProtection(KeyRingProtectionMode.Certificate, current, previous);
    }

    /// <summary>
    ///     This method sets the key ring's encryption on <paramref name="builder" /> to match
    ///     <see cref="Mode" />, and the certificates it decrypts with.
    /// </summary>
    /// <param name="builder">The Data Protection builder to configure.</param>
    /// <returns>The same builder.</returns>
    public IDataProtectionBuilder Apply(IDataProtectionBuilder builder)
    {
        switch (Mode)
        {
            case KeyRingProtectionMode.Certificate:
                builder.ProtectKeysWithCertificate(_current);

                // The decryptor does not use the certificate that encrypts. Without this list it
                // searches the platform certificate store, which does not hold a file-loaded
                // certificate, so a key encrypted under it could not be read back. The previous
                // certificate is listed so entries written before a certificate change still open.
                builder.UnprotectKeysWithAnyCertificate(
                    _previous is null ? [_current] : [_current, _previous]
                );
                break;

            case KeyRingProtectionMode.Dpapi:
                // Resolve selects this mode only on Windows; the check also satisfies the
                // platform analyzer and fails loudly if the mode is ever reached on another platform.
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException(
                        "DPAPI protection is available on Windows only."
                    );

                builder.ProtectKeysWithDpapi(protectToLocalMachine: false);
                break;
        }

        return builder;
    }

    /// <summary>
    ///     This method releases the loaded certificates.
    /// </summary>
    public void Dispose()
    {
        _current?.Dispose();
        _previous?.Dispose();
    }

    /// <summary>
    ///     This method loads one PKCS#12 file and checks it can encrypt and decrypt key ring
    ///     entries.
    /// </summary>
    private static X509Certificate2 Load(
        string path,
        string password,
        string pathSetting,
        string passwordSetting
    )
    {
        if (!File.Exists(path))
            throw new DatabaseStartupRefusedException(
                $"{pathSetting} names the file '{path}', which does not exist or cannot be read. "
                    + "Ninlil will not start the key ring without it: it would write new keys unencrypted "
                    + "and could not read keys the certificate encrypted. Fix the path or clear the setting."
            );

        X509Certificate2 certificate;
        try
        {
            // The key stays in memory. On Windows a default load would write a key file into the
            // account's profile that nothing deletes. macOS does not support the ephemeral flag.
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                path,
                string.IsNullOrEmpty(password) ? null : password,
                OperatingSystem.IsWindows()
                    ? X509KeyStorageFlags.EphemeralKeySet
                    : X509KeyStorageFlags.DefaultKeySet
            );
        }
        catch (Exception exception) when (exception is CryptographicException or IOException)
        {
            // The password is never part of the message.
            throw new DatabaseStartupRefusedException(
                $"{pathSetting} names the file '{path}', which could not be opened as a PKCS#12 file. "
                    + $"The usual cause is a wrong or missing password: check {passwordSetting}. "
                    + $"The reason reported was: {exception.Message}",
                exception
            );
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new DatabaseStartupRefusedException(
                $"The certificate in {pathSetting} ('{path}') has no private key. The key ring needs the "
                    + "private key to decrypt its entries. Export the certificate with its private key."
            );
        }

        // Data Protection wraps the key ring's content key with RSA (XML encryption key transport).
        // Any other key type fails later, at the first key write, with a less helpful message.
        if (certificate.GetRSAPublicKey() is null)
        {
            certificate.Dispose();
            throw new DatabaseStartupRefusedException(
                $"The certificate in {pathSetting} ('{path}') does not hold an RSA key. The key ring can be "
                    + "encrypted only with an RSA certificate."
            );
        }

        return certificate;
    }
}
