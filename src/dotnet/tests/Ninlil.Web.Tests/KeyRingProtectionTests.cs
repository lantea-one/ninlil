using System.Security.Cryptography;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Web.DataProtection;
using Ninlil.Web.Options;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests how the Data Protection key ring is encrypted at rest: the order of the
///     settings (certificate, DPAPI, none), that a misconfigured certificate refuses the start,
///     and that keys written under a certificate, a previous certificate or no protection all
///     read back. It builds real key rings in temporary directories with certificates generated in
///     process; no host is started. The DPAPI tests run on Windows only and return early
///     elsewhere.
/// </summary>
[Collection(WebTestCollection.Name)]
public sealed class KeyRingProtectionTests : IDisposable
{
    private readonly string _directory = KeyRingTestCertificates.NewDirectory();

    /// <summary>
    ///     This method deletes the temporary directory.
    /// </summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    ///     With no certificate, a platform other than Windows leaves new keys unencrypted.
    /// </summary>
    [Fact]
    public void Resolve_WithoutACertificateOffWindows_LeavesKeysUnencrypted()
    {
        using KeyRingProtection protection = KeyRingProtection.Resolve(
            new DataProtectionKeyRingOptions(),
            isWindows: false
        );

        Assert.Equal(KeyRingProtectionMode.None, protection.Mode);
    }

    /// <summary>
    ///     With no certificate, Windows uses DPAPI.
    /// </summary>
    [Fact]
    public void Resolve_WithoutACertificateOnWindows_UsesDpapi()
    {
        using KeyRingProtection protection = KeyRingProtection.Resolve(
            new DataProtectionKeyRingOptions(),
            isWindows: true
        );

        Assert.Equal(KeyRingProtectionMode.Dpapi, protection.Mode);
    }

    /// <summary>
    ///     A configured certificate wins over DPAPI, so Windows installs can opt out of it.
    /// </summary>
    [Fact]
    public void Resolve_WithACertificateOnWindows_UsesTheCertificate()
    {
        string pfx = Path.Combine(_directory, "current.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfx, "pw");

        using KeyRingProtection protection = KeyRingProtection.Resolve(
            new DataProtectionKeyRingOptions { CertificatePath = pfx, CertificatePassword = "pw" },
            isWindows: true
        );

        Assert.Equal(KeyRingProtectionMode.Certificate, protection.Mode);
    }

    /// <summary>
    ///     A path that does not exist refuses the start and names the setting.
    /// </summary>
    [Fact]
    public void Resolve_WithAMissingFile_RefusesAndNamesTheSetting()
    {
        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                KeyRingProtection.Resolve(
                    new DataProtectionKeyRingOptions
                    {
                        CertificatePath = Path.Combine(_directory, "missing.pfx"),
                    },
                    isWindows: false
                )
        );

        Assert.Contains("DataProtection:CertificatePath", refusal.Message);
        Assert.Contains("does not exist", refusal.Message);
    }

    /// <summary>
    ///     A wrong password refuses the start, names both settings and never prints the password.
    /// </summary>
    [Fact]
    public void Resolve_WithAWrongPassword_RefusesAndNamesThePasswordSettingWithoutIt()
    {
        string pfx = Path.Combine(_directory, "current.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfx, "right-password");

        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                KeyRingProtection.Resolve(
                    new DataProtectionKeyRingOptions
                    {
                        CertificatePath = pfx,
                        CertificatePassword = "wrong-secret-password",
                    },
                    isWindows: false
                )
        );

        Assert.Contains("DataProtection:CertificatePath", refusal.Message);
        Assert.Contains("DataProtection:CertificatePassword", refusal.Message);
        Assert.DoesNotContain("wrong-secret-password", refusal.Message);
        Assert.DoesNotContain("right-password", refusal.Message);
    }

    /// <summary>
    ///     A certificate without a private key refuses the start.
    /// </summary>
    [Fact]
    public void Resolve_WithACertificateWithoutAPrivateKey_RefusesAndNamesTheSetting()
    {
        string pfx = Path.Combine(_directory, "public.pfx");
        KeyRingTestCertificates.WritePublicOnlyPfx(pfx);

        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                KeyRingProtection.Resolve(
                    new DataProtectionKeyRingOptions { CertificatePath = pfx },
                    isWindows: false
                )
        );

        Assert.Contains("DataProtection:CertificatePath", refusal.Message);
        Assert.Contains("no private key", refusal.Message);
    }

    /// <summary>
    ///     A certificate that is not RSA refuses the start, because the key ring wraps its keys
    ///     with RSA.
    /// </summary>
    [Fact]
    public void Resolve_WithAnEcdsaCertificate_RefusesAndNamesTheSetting()
    {
        string pfx = Path.Combine(_directory, "ecdsa.pfx");
        KeyRingTestCertificates.WriteEcdsaPfx(pfx);

        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                KeyRingProtection.Resolve(
                    new DataProtectionKeyRingOptions { CertificatePath = pfx },
                    isWindows: false
                )
        );

        Assert.Contains("DataProtection:CertificatePath", refusal.Message);
        Assert.Contains("RSA", refusal.Message);
    }

    /// <summary>
    ///     A broken previous certificate refuses the start and names the previous-certificate
    ///     settings.
    /// </summary>
    [Fact]
    public void Resolve_WithAMissingPreviousFile_RefusesAndNamesThePreviousSetting()
    {
        string pfx = Path.Combine(_directory, "current.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfx, null);

        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                KeyRingProtection.Resolve(
                    new DataProtectionKeyRingOptions
                    {
                        CertificatePath = pfx,
                        PreviousCertificatePath = Path.Combine(_directory, "gone.pfx"),
                    },
                    isWindows: false
                )
        );

        Assert.Contains("DataProtection:PreviousCertificatePath", refusal.Message);
    }

    /// <summary>
    ///     A previous certificate without a current one refuses the start, because dropping the
    ///     current certificate would otherwise fall back to unencrypted keys unnoticed.
    /// </summary>
    [Fact]
    public void Resolve_WithAPreviousCertificateOnly_Refuses()
    {
        string pfx = Path.Combine(_directory, "previous.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfx, null);

        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                KeyRingProtection.Resolve(
                    new DataProtectionKeyRingOptions { PreviousCertificatePath = pfx },
                    isWindows: false
                )
        );

        Assert.Contains("DataProtection:PreviousCertificatePath", refusal.Message);
        Assert.Contains("DataProtection:CertificatePath", refusal.Message);
    }

    /// <summary>
    ///     With a certificate, a new key file holds no plaintext master key, and the payload
    ///     protected with it reads back from a fresh key ring over the same directory. The
    ///     fresh ring is what loading the certificate from its file on restart looks like.
    /// </summary>
    [Fact]
    public void KeyRing_WithACertificate_WritesNoPlaintextMasterKeyAndReadsBack()
    {
        string pfx = Path.Combine(_directory, "current.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfx, "pw");
        string ringDirectory = Path.Combine(_directory, "ring");

        byte[] protectedBytes;
        using (KeyRingProtection protection = ResolveCertificate(pfx, "pw"))
            protectedBytes = KeyRingTestRings.Protect(ringDirectory, protection, "secret-one");

        string keyFile = Assert.Single(Directory.GetFiles(ringDirectory, "key-*.xml"));
        string xml = File.ReadAllText(keyFile);
        Assert.DoesNotContain("<masterKey", xml);
        Assert.DoesNotContain("<value>", xml);
        Assert.Contains("encryptedSecret", xml);

        using KeyRingProtection reloaded = ResolveCertificate(pfx, "pw");
        Assert.Equal(
            "secret-one",
            KeyRingTestRings.Unprotect(ringDirectory, reloaded, protectedBytes)
        );
    }

    /// <summary>
    ///     Without protection the same key file does hold the plaintext master key. This guards
    ///     the assertion above: it must be able to tell the two apart.
    /// </summary>
    [Fact]
    public void KeyRing_WithoutProtection_WritesAPlaintextMasterKey()
    {
        string ringDirectory = Path.Combine(_directory, "ring");
        using KeyRingProtection none = KeyRingProtection.Resolve(
            new DataProtectionKeyRingOptions(),
            isWindows: false
        );
        KeyRingTestRings.Protect(ringDirectory, none, "secret-one");

        string xml = File.ReadAllText(
            Assert.Single(Directory.GetFiles(ringDirectory, "key-*.xml"))
        );
        Assert.Contains("<masterKey", xml);
        Assert.Contains("<value>", xml);
    }

    /// <summary>
    ///     A secret stored before a certificate is configured still decrypts afterwards, and a key
    ///     created afterwards is encrypted.
    /// </summary>
    [Fact]
    public void KeyRing_WhenACertificateIsAddedLater_StillReadsOldKeysAndEncryptsNewOnes()
    {
        string pfx = Path.Combine(_directory, "current.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfx, null);
        string ringDirectory = Path.Combine(_directory, "ring");

        byte[] before;
        using (
            KeyRingProtection none = KeyRingProtection.Resolve(
                new DataProtectionKeyRingOptions(),
                isWindows: false
            )
        )
            before = KeyRingTestRings.Protect(ringDirectory, none, "stored-before");

        using KeyRingProtection certificate = ResolveCertificate(pfx, null);
        Assert.Equal(
            "stored-before",
            KeyRingTestRings.Unprotect(ringDirectory, certificate, before)
        );

        // A new default key is created, as happens when the old one nears its expiry.
        byte[] after = KeyRingTestRings.ProtectWithNewKey(
            ringDirectory,
            certificate,
            "stored-after"
        );
        string[] files = Directory.GetFiles(ringDirectory, "key-*.xml");
        Assert.Equal(2, files.Length);
        Assert.Single(files, file => !File.ReadAllText(file).Contains("<masterKey"));
        Assert.Equal("stored-after", KeyRingTestRings.Unprotect(ringDirectory, certificate, after));
        Assert.Equal(
            "stored-before",
            KeyRingTestRings.Unprotect(ringDirectory, certificate, before)
        );
    }

    /// <summary>
    ///     After the certificate is replaced, entries written under the old one decrypt while it
    ///     is set as the previous certificate, and fail without it.
    /// </summary>
    [Fact]
    public void KeyRing_WhenTheCertificateIsReplaced_ReadsOldKeysOnlyWithThePreviousCertificate()
    {
        string oldPfx = Path.Combine(_directory, "old.pfx");
        string newPfx = Path.Combine(_directory, "new.pfx");
        KeyRingTestCertificates.WriteRsaPfx(oldPfx, "old");
        KeyRingTestCertificates.WriteRsaPfx(newPfx, "new");
        string ringDirectory = Path.Combine(_directory, "ring");

        byte[] underOld;
        using (KeyRingProtection oldProtection = ResolveCertificate(oldPfx, "old"))
            underOld = KeyRingTestRings.Protect(ringDirectory, oldProtection, "old-secret");

        using (
            KeyRingProtection both = KeyRingProtection.Resolve(
                new DataProtectionKeyRingOptions
                {
                    CertificatePath = newPfx,
                    CertificatePassword = "new",
                    PreviousCertificatePath = oldPfx,
                    PreviousCertificatePassword = "old",
                },
                isWindows: false
            )
        )
        {
            Assert.Equal("old-secret", KeyRingTestRings.Unprotect(ringDirectory, both, underOld));

            // New entries are written under the new certificate.
            byte[] underNew = KeyRingTestRings.ProtectWithNewKey(ringDirectory, both, "new-secret");
            Assert.Equal("new-secret", KeyRingTestRings.Unprotect(ringDirectory, both, underNew));
        }

        using KeyRingProtection onlyNew = ResolveCertificate(newPfx, "new");
        Assert.ThrowsAny<CryptographicException>(() =>
            KeyRingTestRings.Unprotect(ringDirectory, onlyNew, underOld)
        );
    }

    /// <summary>
    ///     On Windows, DPAPI-protected keys write no plaintext master key and still read after a
    ///     certificate is added. Elsewhere there is nothing to run.
    /// </summary>
    [Fact]
    public void KeyRing_OnWindowsWithDpapi_EncryptsAndStillReadsAfterACertificateIsAdded()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string pfx = Path.Combine(_directory, "current.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfx, null);
        string ringDirectory = Path.Combine(_directory, "ring");

        byte[] underDpapi;
        using (
            KeyRingProtection dpapi = KeyRingProtection.Resolve(
                new DataProtectionKeyRingOptions(),
                isWindows: true
            )
        )
        {
            underDpapi = KeyRingTestRings.Protect(ringDirectory, dpapi, "dpapi-secret");
            string xml = File.ReadAllText(
                Assert.Single(Directory.GetFiles(ringDirectory, "key-*.xml"))
            );
            // DPAPI keeps its ciphertext in a <value> element too, so only a bare <masterKey>
            // marks a plaintext key here.
            Assert.DoesNotContain("<masterKey", xml);
            Assert.Contains("DpapiXmlDecryptor", xml);
        }

        using KeyRingProtection certificate = ResolveCertificate(pfx, null);
        Assert.Equal(
            "dpapi-secret",
            KeyRingTestRings.Unprotect(ringDirectory, certificate, underDpapi)
        );

        byte[] underCertificate = KeyRingTestRings.ProtectWithNewKey(
            ringDirectory,
            certificate,
            "cert-secret"
        );
        Assert.Equal(
            "cert-secret",
            KeyRingTestRings.Unprotect(ringDirectory, certificate, underCertificate)
        );
    }

    private static KeyRingProtection ResolveCertificate(string path, string password) =>
        KeyRingProtection.Resolve(
            new DataProtectionKeyRingOptions
            {
                CertificatePath = path,
                CertificatePassword = password ?? "",
            },
            isWindows: OperatingSystem.IsWindows()
        );
}
