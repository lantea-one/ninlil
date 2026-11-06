using System.Text;
using Ninlil.Web.DataProtection;
using Ninlil.Web.Options;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="KeyRingRestoreCheck" />: which key files of a backup it accepts
///     and the cause and setting its refusal names. The key files are written by real key rings
///     in temporary directories, protected with certificates generated in process; no host is
///     started.
/// </summary>
[Collection(WebTestCollection.Name)]
public sealed class KeyRingRestoreCheckTests : IDisposable
{
    private readonly string _directory = KeyRingTestCertificates.NewDirectory();

    /// <summary>
    ///     This method deletes the temporary directory.
    /// </summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    ///     Keys written by the same certificate are readable, so a restore on the same host
    ///     passes.
    /// </summary>
    [Fact]
    public void Check_WithKeysFromTheSameCertificate_AcceptsThem()
    {
        string pfx = NewPfx("current.pfx");
        using KeyRingProtection protection = Certificate(pfx);
        (string name, byte[] bytes) = WriteKeyFile(protection);

        string accepted = new KeyRingRestoreCheck(protection).Check([(name, bytes)]);
        Assert.True(accepted is null, accepted);
    }

    /// <summary>
    ///     Unencrypted keys are readable under any protection: a backup taken before the
    ///     certificate was configured still restores.
    /// </summary>
    [Fact]
    public void Check_WithUnencryptedKeys_AcceptsThemUnderACertificate()
    {
        using KeyRingProtection none = None();
        (string name, byte[] bytes) = WriteKeyFile(none);

        using KeyRingProtection certificate = Certificate(NewPfx("current.pfx"));
        string accepted = new KeyRingRestoreCheck(certificate).Check([(name, bytes)]);
        Assert.True(accepted is null, accepted);
    }

    /// <summary>
    ///     Keys from a certificate this server does not hold are refused with a message that
    ///     names the previous-certificate setting.
    /// </summary>
    [Fact]
    public void Check_WithKeysFromAnotherCertificate_NamesThePreviousCertificateSetting()
    {
        using KeyRingProtection other = Certificate(NewPfx("other.pfx"));
        (string name, byte[] bytes) = WriteKeyFile(other);

        using KeyRingProtection current = Certificate(NewPfx("current.pfx"));
        string message = new KeyRingRestoreCheck(current).Check([(name, bytes)]);

        Assert.NotNull(message);
        Assert.Contains(name, message);
        Assert.Contains("certificate", message);
        Assert.Contains("DataProtection:PreviousCertificatePath", message);
        Assert.Contains("Nothing was restored", message);
    }

    /// <summary>
    ///     The same keys are accepted once the other certificate is configured as the previous
    ///     one.
    /// </summary>
    [Fact]
    public void Check_WithKeysFromThePreviousCertificate_AcceptsThem()
    {
        string otherPfx = NewPfx("other.pfx");
        using KeyRingProtection other = Certificate(otherPfx);
        (string name, byte[] bytes) = WriteKeyFile(other);

        using KeyRingProtection both = KeyRingProtection.Resolve(
            new DataProtectionKeyRingOptions
            {
                CertificatePath = NewPfx("current.pfx"),
                PreviousCertificatePath = otherPfx,
            },
            isWindows: false
        );

        string accepted = new KeyRingRestoreCheck(both).Check([(name, bytes)]);
        Assert.True(accepted is null, accepted);
    }

    /// <summary>
    ///     Certificate keys on a server with no certificate are refused with a message that names
    ///     the certificate setting.
    /// </summary>
    [Fact]
    public void Check_WithCertificateKeysAndNoCertificate_NamesTheCertificateSetting()
    {
        using KeyRingProtection certificate = Certificate(NewPfx("current.pfx"));
        (string name, byte[] bytes) = WriteKeyFile(certificate);

        using KeyRingProtection none = None();
        string message = new KeyRingRestoreCheck(none).Check([(name, bytes)]);

        Assert.NotNull(message);
        Assert.Contains("no certificate is configured", message);
        Assert.Contains("DataProtection:CertificatePath", message);
    }

    /// <summary>
    ///     A DPAPI key that this server cannot decrypt is refused with a message that says DPAPI
    ///     is tied to the machine and account and names the certificate setting. The file is a
    ///     real unencrypted key whose secret is swapped for a DPAPI decryptor element, which is
    ///     how a DPAPI key from another machine looks to this server; off Windows no DPAPI call
    ///     succeeds, and on Windows the blob is not one this account protected.
    /// </summary>
    [Fact]
    public void Check_WithADpapiKeyThisServerCannotRead_NamesDpapiAndTheCertificateSetting()
    {
        using KeyRingProtection none = None();
        (string name, byte[] plain) = WriteKeyFile(none);
        string xml = Encoding.UTF8.GetString(plain);
        int start = xml.IndexOf("<masterKey", StringComparison.Ordinal);
        int end = xml.IndexOf("</masterKey>", StringComparison.Ordinal) + "</masterKey>".Length;
        string dpapi =
            xml[..start]
            + "<encryptedSecret decryptorType=\"Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlDecryptor, Microsoft.AspNetCore.DataProtection\" xmlns=\"http://schemas.asp.net/2015/03/dataProtection\">"
            + "<!-- Windows DPAPI --><value>AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA</value></encryptedSecret>"
            + xml[end..];

        string message = new KeyRingRestoreCheck(none).Check([
            (name, Encoding.UTF8.GetBytes(dpapi)),
        ]);

        Assert.NotNull(message);
        Assert.Contains("DPAPI", message);
        Assert.Contains("DataProtection:CertificatePath", message);
        Assert.Contains("Nothing was restored", message);
    }

    private string NewPfx(string fileName)
    {
        string path = Path.Combine(_directory, fileName);
        KeyRingTestCertificates.WriteRsaPfx(path, null);
        return path;
    }

    private static KeyRingProtection Certificate(string pfx) =>
        KeyRingProtection.Resolve(
            new DataProtectionKeyRingOptions { CertificatePath = pfx },
            isWindows: false
        );

    private static KeyRingProtection None() =>
        KeyRingProtection.Resolve(new DataProtectionKeyRingOptions(), isWindows: false);

    /// <summary>
    ///     This method writes a key into a fresh ring directory with the given protection and
    ///     returns the key file's name and bytes.
    /// </summary>
    private (string Name, byte[] Bytes) WriteKeyFile(KeyRingProtection protection)
    {
        string ring = Path.Combine(_directory, $"ring-{Guid.NewGuid():N}");
        KeyRingTestRings.Protect(ring, protection, "x");
        string file = Directory.GetFiles(ring, "key-*.xml").Single();
        return (Path.GetFileName(file), File.ReadAllBytes(file));
    }
}
