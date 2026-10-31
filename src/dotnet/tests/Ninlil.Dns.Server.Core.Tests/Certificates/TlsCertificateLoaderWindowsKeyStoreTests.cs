using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Certificates;

/// <summary>
///     This class tests that on Windows <see cref="TlsCertificateLoader" /> keeps one persisted
///     private key per certificate in the current user's certificate store, instead of a new key
///     file in the CNG key store on every load, and removes the key of a replaced certificate.
///     The tests use the real current-user store and key store, with a temporary directory for
///     the certificate files, and remove every entry and key they create. They return at once
///     on other platforms, where the loader keeps keys in memory and leaves no file.
/// </summary>
public sealed class TlsCertificateLoaderWindowsKeyStoreTests : IDisposable
{
    private readonly string _directory = Directory
        .CreateTempSubdirectory("ninlil-tls-store-tests-")
        .FullName;

    private readonly List<string> _sources = [];

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
            foreach (string source in _sources)
                WindowsCertificateKeyStore.RemoveEntries(source, NullLogger.Instance);

        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    ///     Every load of the same certificate must use the same key file. Without the store
    ///     each load of the PFX creates its own key container, which nothing deletes.
    /// </summary>
    [Fact]
    public void Load_TheSameCertificateManyTimes_UsesOneKeyFile()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string pfxPath = WritePfx("first.pfx", "CN=first");
        TlsCertificateOptions options = new() { PfxPath = pfxPath };
        HashSet<string> keyNames = [];

        for (int load = 0; load < 5; load++)
        {
            using X509Certificate2 certificate = TlsCertificateLoader.LoadOrGenerate(
                options,
                NullLogger.Instance
            );
            keyNames.Add(KeyName(certificate));
        }

        Assert.Single(keyNames);
        Assert.True(CngKey.Exists(keyNames.Single()));
    }

    /// <summary>
    ///     A certificate file replaced on disk and reloaded leaves its predecessor's store entry
    ///     and key only until the grace period (three reload intervals) passes.
    /// </summary>
    [Fact]
    public async Task Load_ACertificateFileThatChanges_RemovesTheReplacedEntryAndKey()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string pfxPath = WritePfx("swapped.pfx", "CN=old");
        TlsCertificateOptions options = new()
        {
            PfxPath = pfxPath,
            CertificateReloadPollInterval = TimeSpan.FromMilliseconds(50),
        };
        string oldThumbprint;
        string oldKeyName;

        using (
            X509Certificate2 old = TlsCertificateLoader.LoadOrGenerate(options, NullLogger.Instance)
        )
        {
            oldThumbprint = old.Thumbprint;
            oldKeyName = KeyName(old);
        }

        Assert.True(StoreHolds(oldThumbprint));

        WritePfx("swapped.pfx", "CN=new");
        using X509Certificate2 replacement = TlsCertificateLoader.LoadOrGenerate(
            options,
            NullLogger.Instance
        );

        Assert.NotEqual(oldThumbprint, replacement.Thumbprint);

        DateTime deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline && StoreHolds(oldThumbprint))
            await Task.Delay(50);

        Assert.False(StoreHolds(oldThumbprint));
        Assert.False(CngKey.Exists(oldKeyName));
        Assert.True(StoreHolds(replacement.Thumbprint));
        Assert.True(CngKey.Exists(KeyName(replacement)));
    }

    /// <summary>
    ///     Writes a new self-signed certificate with the given subject to a PFX file in the test
    ///     directory, replacing the file if it exists, and remembers the path for clean-up.
    /// </summary>
    private string WritePfx(string fileName, string subject)
    {
        string path = Path.Combine(_directory, fileName);

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256);
        using X509Certificate2 created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30)
        );
        File.WriteAllBytes(path, created.Export(X509ContentType.Pfx));

        if (!_sources.Contains(path))
            _sources.Add(path);

        return path;
    }

    /// <summary>
    ///     Returns the name of the CNG key container behind the certificate's private key.
    /// </summary>
    private static string KeyName(X509Certificate2 certificate)
    {
        using ECDsa key = certificate.GetECDsaPrivateKey();

        return ((ECDsaCng)key).Key.UniqueName;
    }

    /// <summary>
    ///     Reports whether the current user's <c>My</c> store holds the thumbprint.
    /// </summary>
    private static bool StoreHolds(string thumbprint)
    {
        using X509Store store = new(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false).Count > 0;
    }
}
