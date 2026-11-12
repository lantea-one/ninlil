using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Certificates;

/// <summary>
///     This class tests the shared test clean-up helper <see cref="TlsCertificateStore" />
///     against <see cref="TlsCertificateLoader" />: it must find the store entries the loader
///     tagged for a source path, and delete the persisted CNG key with each entry. The tests use
///     the real current-user store and key store and return at once on other platforms, where
///     the loader creates neither.
/// </summary>
public sealed class TlsCertificateStoreCleanupTests : IDisposable
{
    private readonly string _directory = Directory
        .CreateTempSubdirectory("ninlil-tls-cleanup-tests-")
        .FullName;

    public void Dispose()
    {
        TemporaryFiles.DeleteDirectory(_directory);
    }

    /// <summary>
    ///     The helper repeats the loader's tag format because the loader's key store is internal.
    ///     A tag that drifts makes every clean-up miss its entries silently.
    /// </summary>
    [Fact]
    public void TagFor_AnyPath_MatchesTheTagTheLoaderWritesToTheStore()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string path = Path.Combine(_directory, "tag.pfx");

        Assert.Equal(WindowsCertificateKeyStore.TagFor(path), TlsCertificateStore.TagFor(path));
    }

    /// <summary>
    ///     Removing the entries of a source path deletes the entry and its key file, so a run
    ///     that loaded the certificate leaves nothing in the user's stores.
    /// </summary>
    [Fact]
    public void RemoveEntriesForSource_AfterTheLoaderImportedTheCertificate_DeletesTheEntryAndItsKey()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string path = WritePfx("source.pfx");
        string thumbprint;
        string keyName;

        using (X509Certificate2 loaded = Load(path))
        {
            thumbprint = loaded.Thumbprint;
            keyName = KeyName(loaded);
        }

        Assert.True(StoreHolds(thumbprint));
        Assert.True(CngKey.Exists(keyName));

        TlsCertificateStore.RemoveEntriesForSource(path);

        Assert.False(StoreHolds(thumbprint));
        Assert.False(CngKey.Exists(keyName));
    }

    /// <summary>
    ///     Deleting a directory removes the entries of every certificate file below it, which
    ///     is how the test fixtures that use a temporary directory clean up.
    /// </summary>
    [Fact]
    public void DeleteDirectory_AfterTheLoaderImportedCertificatesFromIt_DeletesTheirEntriesAndKeys()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string directory = Directory.CreateTempSubdirectory("ninlil-tls-cleanup-tree-").FullName;
        string path = WritePfx("nested.pfx", directory);
        string thumbprint;
        string keyName;

        using (X509Certificate2 loaded = Load(path))
        {
            thumbprint = loaded.Thumbprint;
            keyName = KeyName(loaded);
        }

        TemporaryFiles.DeleteDirectory(directory);

        Assert.False(Directory.Exists(directory));
        Assert.False(StoreHolds(thumbprint));
        Assert.False(CngKey.Exists(keyName));
    }

    /// <summary>
    ///     Removing a source the loader never imported is a no-op, so a fixture whose test failed
    ///     before the load still disposes cleanly.
    /// </summary>
    [Fact]
    public void RemoveEntriesForSource_ForAPathNothingWasLoadedFrom_DoesNothing()
    {
        TlsCertificateStore.RemoveEntriesForSource(Path.Combine(_directory, "never-loaded.pfx"));
        TlsCertificateStore.RemoveEntriesForSource(null);
    }

    private static X509Certificate2 Load(string path) =>
        TlsCertificateLoader.LoadOrGenerate(
            new TlsCertificateOptions { PfxPath = path },
            NullLogger.Instance
        );

    /// <summary>Writes a new self-signed certificate to a PFX file and returns its path.</summary>
    private string WritePfx(string fileName, string directory = null)
    {
        string path = Path.Combine(directory ?? _directory, fileName);

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=cleanup", key, HashAlgorithmName.SHA256);
        using X509Certificate2 created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30)
        );
        File.WriteAllBytes(path, created.Export(X509ContentType.Pfx));

        return path;
    }

    /// <summary>Returns the name of the CNG key container behind the certificate.</summary>
    private static string KeyName(X509Certificate2 certificate)
    {
        using ECDsa key = certificate.GetECDsaPrivateKey();

        return ((ECDsaCng)key).Key.UniqueName;
    }

    /// <summary>Reports whether the current user's <c>My</c> store holds the thumbprint.</summary>
    private static bool StoreHolds(string thumbprint)
    {
        using X509Store store = new(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false).Count > 0;
    }
}
