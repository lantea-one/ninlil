using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Certificates;

/// <summary>
///     This class gives a TLS certificate loaded from a PKCS#12 file one persisted private key
///     on Windows, instead of a new key file on every load.
///     <para>
///         Schannel cannot use an ephemeral key for a server handshake, so the certificate must
///         be loaded with a persisted key. Loading a PFX that way creates a key file in the
///         account's CNG key store (<c>%APPDATA%\Microsoft\Crypto\Keys</c>, or the system profile
///         for LocalSystem) that nothing deletes, and every transport loads the certificate on
///         every start. This class imports each certificate once into the current account's
///         <c>My</c> store, finds it again by thumbprint on later loads and starts, and deletes
///         the entry and its key when the file holds a different certificate.
///     </para>
///     <para>
///         Entries are tagged with a friendly name that is a hash of the source file's path, so
///         two sources (for example a custom PFX per transport, or two Ninlil instances in one
///         account) never remove each other's entries. A source whose configured path changes
///         leaves its last entry behind.
///     </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsCertificateKeyStore
{
    private const string FriendlyNamePrefix = "Ninlil TLS ";

    private static readonly object Gate = new();

    // The thumbprint each tag resolved to most recently in this process.
    private static readonly Dictionary<string, string> CurrentByTag = [];

    // One pending clean-up per tag. The map keeps the timers reachable until they fire.
    private static readonly Dictionary<string, Timer> PendingCleanups = [];

    /// <summary>
    ///     This method returns the certificate <paramref name="load" /> reads, with its private
    ///     key persisted in the current account's <c>My</c> store, importing it if it is not
    ///     there yet. When the certificate store cannot be used it logs a warning and returns a
    ///     plainly loaded certificate, which works but leaves a key file per load.
    /// </summary>
    /// <param name="load">Loads the PKCS#12 data with the given key storage flags. It throws
    /// <see cref="CryptographicException" /> for data it cannot read, which this method passes on.</param>
    /// <param name="sourceIdentity">The path of the file the data comes from; it names the
    /// entries this source owns.</param>
    /// <param name="supersededKeyGracePeriod">How long a replaced certificate's key stays after a
    /// reload, so a transport that has not reloaded yet can finish handshakes with it.</param>
    /// <param name="logger">The logger this method records an import, a removal or a store failure to.</param>
    public static X509Certificate2 Resolve(
        Func<X509KeyStorageFlags, X509Certificate2> load,
        string sourceIdentity,
        TimeSpan supersededKeyGracePeriod,
        ILogger logger
    )
    {
        // The thumbprint identifies the certificate without creating a key. A file the loader
        // cannot read fails here, as it does for every other platform.
        string thumbprint;
        using (X509Certificate2 probe = load(X509KeyStorageFlags.EphemeralKeySet))
            thumbprint = probe.Thumbprint;

        string tag = TagFor(sourceIdentity);

        try
        {
            lock (Gate)
            {
                X509Certificate2 certificate = FindOrImport(load, thumbprint, tag, logger);
                Track(tag, thumbprint, supersededKeyGracePeriod, logger);

                return certificate;
            }
        }
        catch (Exception exception)
            when (exception is CryptographicException or UnauthorizedAccessException)
        {
            // A service account without a loaded profile has no usable user store. The
            // certificate still serves; only the key-file clean-up is lost.
            logger.LogWarning(
                exception,
                "The Windows certificate store could not hold the TLS certificate from '{Path}'; loading it without the store, which leaves a key file in the account's key store on every start.",
                sourceIdentity
            );

            return load(X509KeyStorageFlags.DefaultKeySet);
        }
    }

    /// <summary>
    ///     This method returns the friendly name that marks the store entries of the source file
    ///     at <paramref name="sourceIdentity" />: a fixed prefix and a hash of its full path.
    /// </summary>
    internal static string TagFor(string sourceIdentity) =>
        FriendlyNamePrefix
        + Convert.ToHexString(
            SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    Path.GetFullPath(sourceIdentity).ToUpperInvariant()
                )
            )
        )[..16];

    /// <summary>
    ///     This method deletes every store entry of the source file at
    ///     <paramref name="sourceIdentity" />, with its private key. Tests use it to leave the
    ///     account's stores as they found them.
    /// </summary>
    internal static void RemoveEntries(string sourceIdentity, ILogger logger)
    {
        lock (Gate)
            Prune(TagFor(sourceIdentity), null, logger);
    }

    /// <summary>
    ///     This method returns the store's entry for <paramref name="thumbprint" /> when its key
    ///     works, and otherwise imports the certificate with a persisted key.
    /// </summary>
    private static X509Certificate2 FindOrImport(
        Func<X509KeyStorageFlags, X509Certificate2> load,
        string thumbprint,
        string tag,
        ILogger logger
    )
    {
        using X509Store store = new(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        foreach (
            X509Certificate2 entry in store.Certificates.Find(
                X509FindType.FindByThumbprint,
                thumbprint,
                false
            )
        )
        {
            if (HasUsableKey(entry))
                return entry;

            // The entry lost its key file (someone deleted it). Replace the entry.
            DeleteKey(entry, logger);
            store.Remove(entry);
            entry.Dispose();
        }

        X509Certificate2 imported = load(X509KeyStorageFlags.PersistKeySet);
        imported.FriendlyName = tag;
        store.Add(imported);

        logger.LogInformation(
            "Imported TLS certificate {Thumbprint} into the current user's certificate store with a persisted key.",
            thumbprint
        );

        return imported;
    }

    /// <summary>
    ///     This method records what <paramref name="tag" /> resolved to and removes the entries
    ///     it superseded: at once for the first resolution in this process (a start), after
    ///     <paramref name="gracePeriod" /> for a later one (a reload).
    /// </summary>
    private static void Track(string tag, string thumbprint, TimeSpan gracePeriod, ILogger logger)
    {
        bool seen = CurrentByTag.TryGetValue(tag, out string previous);

        if (seen && previous == thumbprint)
            return;

        CurrentByTag[tag] = thumbprint;

        if (!seen)
        {
            Prune(tag, thumbprint, logger);

            return;
        }

        if (PendingCleanups.Remove(tag, out Timer replaced))
            replaced.Dispose();

        // Prune keeps whatever is current when the timer fires, so a second reload inside the
        // grace period cannot remove the newest certificate.
        PendingCleanups[tag] = new Timer(
            _ =>
            {
                try
                {
                    lock (Gate)
                    {
                        Prune(tag, CurrentByTag[tag], logger);
                        PendingCleanups.Remove(tag);
                    }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to remove a replaced TLS certificate from the certificate store."
                    );
                }
            },
            null,
            gracePeriod,
            Timeout.InfiniteTimeSpan
        );
    }

    /// <summary>
    ///     This method deletes every store entry tagged <paramref name="tag" /> except
    ///     <paramref name="keep" />, together with its private key.
    /// </summary>
    private static void Prune(string tag, string keep, ILogger logger)
    {
        using X509Store store = new(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        foreach (X509Certificate2 entry in store.Certificates)
        {
            using (entry)
            {
                if (entry.FriendlyName != tag || entry.Thumbprint == keep)
                    continue;

                DeleteKey(entry, logger);
                store.Remove(entry);

                logger.LogInformation(
                    "Removed the superseded TLS certificate {Thumbprint} and its private key from the current user's certificate store.",
                    entry.Thumbprint
                );
            }
        }
    }

    /// <summary>
    ///     This method reports whether the certificate's private key can sign, which fails when
    ///     the key file is gone.
    /// </summary>
    private static bool HasUsableKey(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
            return false;

        try
        {
            byte[] data = new byte[1];

            using (ECDsa ecdsa = certificate.GetECDsaPrivateKey())
            {
                if (ecdsa is not null)
                    return ecdsa.SignData(data, HashAlgorithmName.SHA256).Length > 0;
            }

            using (RSA rsa = certificate.GetRSAPrivateKey())
            {
                return rsa is not null
                    && rsa.SignData(
                        data,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1
                    ).Length > 0;
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method deletes the key file behind the certificate's private key. A failure is
    ///     logged and skipped: the store entry is still removed.
    /// </summary>
    private static void DeleteKey(X509Certificate2 certificate, ILogger logger)
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
            else if (rsa is RSACryptoServiceProvider provider)
            {
                // A legacy provider key is deleted when its container is not marked persistent.
                provider.PersistKeyInCsp = false;
                provider.Clear();
            }
        }
        catch (CryptographicException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to delete the private key of TLS certificate {Thumbprint}.",
                certificate.Thumbprint
            );
        }
    }
}
