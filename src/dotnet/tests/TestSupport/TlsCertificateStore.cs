using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Define our namespace.
namespace Ninlil.TestSupport;

/// <summary>
///     Removes the certificate-store entries and private keys that a test run creates on Windows
///     when it loads a TLS certificate file through <c>TlsCertificateLoader</c>. The file is
///     linked into every test project by <c>src/dotnet/tests/Directory.Build.props</c>, and
///     <see cref="TemporaryFiles" /> calls it when a test deletes a <c>.pfx</c> file or a
///     directory, so most fixtures need no extra code.
/// </summary>
/// <remarks>
///     On Windows the loader imports each certificate into the current user's <c>My</c> store
///     with a persisted CNG key and tags the entry with the friendly name
///     <c>Ninlil TLS &lt;hash of the source path&gt;</c>. Tests use a new directory per run, so
///     nothing else would ever remove those entries or their key files in
///     <c>%APPDATA%\Microsoft\Crypto\Keys</c>. This helper finds the entries by that tag, for the
///     exact source paths a fixture used, so it never touches entries of a real Ninlil install
///     or of another test that runs in parallel. It does nothing on other platforms, where the
///     loader keeps keys in memory.
/// </remarks>
internal static class TlsCertificateStore
{
    // The tag format of WindowsCertificateKeyStore.TagFor in Ninlil.Dns.Server.Core. That type is
    // internal to the Core assembly, so the format is repeated here; a test in
    // Ninlil.Dns.Server.Core.Tests fails when the two drift apart.
    private const string FriendlyNamePrefix = "Ninlil TLS ";

    /// <summary>
    ///     Registers a process-exit clean-up for the certificate the default
    ///     <c>TlsCertificateOptions</c> generate. A host or listener built with default options
    ///     keeps its self-signed certificate at <c>data/protocol-tls-certificate.pfx</c> next to
    ///     the test assembly, a path no fixture owns, so the store entry it imports is removed when
    ///     the test process ends. A process that is killed skips this, like any other exit hook.
    /// </summary>
    // The file is linked into every test project, and Ninlil.Dns.Data.EngineHosts is a library
    // the analyzer does not treat as application code. The initializer only adds an exit hook.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterDefaultCertificateCleanup()
    {
        if (!OperatingSystem.IsWindows())
            return;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => RemoveEntriesForSource(DefaultStatePath);
    }

    /// <summary>
    ///     Gets the self-signed certificate path of a default <c>TlsCertificateOptions</c>, as it
    ///     resolves for the current test assembly.
    /// </summary>
    public static string DefaultStatePath =>
        Path.Combine(AppContext.BaseDirectory, "data", "protocol-tls-certificate.pfx");

    /// <summary>
    ///     Returns the friendly name that marks the store entries of the certificate file at
    ///     <paramref name="sourcePath" />.
    /// </summary>
    /// <param name="sourcePath">The path the certificate was loaded from.</param>
    public static string TagFor(string sourcePath) =>
        FriendlyNamePrefix
        + Convert.ToHexString(
            SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(sourcePath).ToUpperInvariant())
            )
        )[..16];

    /// <summary>
    ///     Deletes the store entries, with their private keys, of the certificate file at
    ///     <paramref name="sourcePath" />. It does nothing on other platforms and never throws for
    ///     a store that cannot be opened.
    /// </summary>
    /// <param name="sourcePath">The path the certificate was loaded from.</param>
    public static void RemoveEntriesForSource(string sourcePath)
    {
        if (!string.IsNullOrEmpty(sourcePath))
            RemoveEntriesForSources([sourcePath]);
    }

    /// <summary>
    ///     Deletes the store entries, with their private keys, of every certificate file in
    ///     <paramref name="sourcePaths" />. It does nothing on other platforms.
    /// </summary>
    /// <param name="sourcePaths">The paths the certificates were loaded from.</param>
    public static void RemoveEntriesForSources(IEnumerable<string> sourcePaths)
    {
        if (!OperatingSystem.IsWindows())
            return;

        HashSet<string> tags = [.. sourcePaths.Select(TagFor)];

        if (tags.Count > 0)
            RemoveEntriesByTag(tags);
    }

    /// <summary>
    ///     Deletes the store entries of every <c>.pfx</c> file in <paramref name="directoryPath" />
    ///     and below. Call it before the directory is deleted: the files name the entries. A
    ///     certificate file the test already deleted by hand is not found, so a test that deletes
    ///     its own file calls <see cref="RemoveEntriesForSource" /> first.
    /// </summary>
    /// <param name="directoryPath">The directory that holds the certificate files.</param>
    public static void RemoveEntriesUnder(string directoryPath)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(directoryPath))
            return;

        RemoveEntriesForSources(
            Directory.EnumerateFiles(directoryPath, "*.pfx", SearchOption.AllDirectories)
        );
    }

    /// <summary>
    ///     Deletes every <c>My</c> store entry whose friendly name is in <paramref name="tags" />,
    ///     with its persisted key. A failure is swallowed: clean-up must not turn a passing test
    ///     into a failing one.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void RemoveEntriesByTag(HashSet<string> tags)
    {
        try
        {
            using X509Store store = new(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            foreach (X509Certificate2 entry in store.Certificates)
            {
                using (entry)
                {
                    if (!tags.Contains(entry.FriendlyName))
                        continue;

                    DeleteKey(entry);
                    store.Remove(entry);
                }
            }
        }
        catch (Exception exception)
            when (exception is CryptographicException or UnauthorizedAccessException)
        {
            // A store that cannot be opened has nothing the loader could have imported either.
        }
    }

    /// <summary>
    ///     Deletes the CNG key file behind the certificate. Removing only the store entry leaves
    ///     the key in <c>%APPDATA%\Microsoft\Crypto\Keys</c>, because the import persisted it.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void DeleteKey(X509Certificate2 certificate)
    {
        try
        {
            using ECDsa ecdsa = certificate.GetECDsaPrivateKey();

            if (ecdsa is ECDsaCng ecdsaCng)
            {
                ecdsaCng.Key.Delete();

                return;
            }

            using RSA rsa = certificate.GetRSAPrivateKey();

            if (rsa is RSACng rsaCng)
                rsaCng.Key.Delete();
        }
        catch (CryptographicException)
        {
            // The key file is already gone; the entry is still removed.
        }
    }
}
