using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Common.Serializer;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.IO;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class implements <see cref="IDnsCryptCertificateStore" /> as one JSON file at
///     <see cref="DnsCryptOptions.CertificateStateFilePath" />, like the RFC 5011 trust anchor
///     store and the self-signed TLS certificate: a small, automatically generated identity does
///     not need the database. The state is loaded on first use of any member, not at
///     construction, so an installation with DNSCrypt off never creates the file or an identity.
///     The load reads the file, or generates a new identity after moving an unparseable file
///     aside. A read error (IO or permission) is not cached and never generates an identity: the
///     member throws <see cref="InvalidOperationException" /> and the next use tries again.
///     <see cref="Save" /> rewrites the whole file atomically.
/// </summary>
/// <param name="options">The DNSCrypt settings, read for the state file path.</param>
/// <param name="logger">The logger this store records a corrupt or unreadable state file to.</param>
public sealed class FileDnsCryptCertificateStore(
    IOptions<DnsCryptOptions> options,
    ILogger<FileDnsCryptCertificateStore> logger
) : IDnsCryptCertificateStore
{
    private readonly Lock _lock = new();

    // A nullable field, not Lazy<T>: Lazy<T> would cache a transient read failure for good.
    private (DnssecKeyPair ProviderKeyPair, List<DnsCryptCertificateEntry> Entries)? _state;

    /// <inheritdoc />
    public DnssecKeyPair ProviderKeyPair
    {
        get
        {
            lock (_lock)
                return EnsureLoaded().ProviderKeyPair;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DnsCryptCertificateEntry> Entries
    {
        get
        {
            lock (_lock)
                return [.. EnsureLoaded().Entries];
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<DnsCryptCertificateEntry> entries)
    {
        lock (_lock)
        {
            (DnssecKeyPair providerKeyPair, List<DnsCryptCertificateEntry> state) = EnsureLoaded();

            state.Clear();
            state.AddRange(entries);
            WriteFile(options.Value.CertificateStateFilePath, providerKeyPair, state, logger);
        }
    }

    /// <summary>
    ///     This method returns the loaded state, loading it on the first call. The caller holds
    ///     <c>_lock</c>. A failed load leaves <c>_state</c> null so the next call retries.
    /// </summary>
    private (
        DnssecKeyPair ProviderKeyPair,
        List<DnsCryptCertificateEntry> Entries
    ) EnsureLoaded() => _state ??= LoadOrSeed(options.Value, logger);

    /// <summary>
    ///     This method loads the state file, or creates a new one with a newly generated provider
    ///     key pair and no certificates when the file does not exist or its content is unusable
    ///     (not JSON, or missing the provider keys or the certificate list). An unusable file is
    ///     moved aside to <c>&lt;path&gt;.corrupt-&lt;timestamp&gt;</c> and an Error is logged.
    ///     A file that cannot be read (IO or permission error) is left in place and no identity is
    ///     generated.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The file exists but cannot be read right now, or the Ed25519 algorithm is missing.
    /// </exception>
    private static (
        DnssecKeyPair ProviderKeyPair,
        List<DnsCryptCertificateEntry> Entries
    ) LoadOrSeed(DnsCryptOptions options, ILogger logger)
    {
        string path = options.CertificateStateFilePath;

        if (File.Exists(path))
        {
            try
            {
                PersistedState loaded = JsonSerializer.Deserialize<PersistedState>(
                    File.ReadAllText(path)
                );

                if (
                    loaded is { ProviderPublicKey.Length: > 0, ProviderPrivateKeyPkcs8.Length: > 0 }
                    && loaded.Certificates is not null
                    && loaded.Certificates.All(IsComplete)
                )
                    return (
                        new DnssecKeyPair(loaded.ProviderPublicKey, loaded.ProviderPrivateKeyPkcs8),
                        [.. loaded.Certificates.Select(ToEntry)]
                    );

                logger.LogError(
                    "The DNSCrypt certificate state file at '{Path}' is incomplete; it was moved aside ({AsidePath}) and a new provider identity is generated. Clients need the new stamp.",
                    path,
                    MoveAside(path)
                );
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // A transient failure (locked file, permissions being fixed) must not replace
                // the identity: the file is the only copy of the provider key that clients pinned.
                logger.LogWarning(
                    exception,
                    "The DNSCrypt certificate state file at '{Path}' could not be read; it is left in place and no new identity is generated.",
                    path
                );

                throw new InvalidOperationException(
                    $"The DNSCrypt certificate state file at '{path}' could not be read; no new identity is generated, and it is tried again on next use.",
                    exception
                );
            }
            catch (Exception exception)
                when (exception is FormatException or System.Text.Json.JsonException)
            {
                logger.LogError(
                    exception,
                    "The DNSCrypt certificate state file at '{Path}' is not valid; it was moved aside ({AsidePath}) and a new provider identity is generated. Clients need the new stamp.",
                    path,
                    MoveAside(path)
                );
            }
        }

        if (
            !DnssecAlgorithmRegistry.TryGetAlgorithm(
                DnssecAlgorithmEnum.Ed25519,
                out IDnssecSigningAlgorithm algorithm
            )
        )
            throw new InvalidOperationException(
                "The Ed25519 signing algorithm, required for DNSCrypt's own provider key, is not registered."
            );

        DnssecKeyPair providerKeyPair = algorithm.GenerateKeyPair();
        List<DnsCryptCertificateEntry> entries = [];

        WriteFile(path, providerKeyPair, entries, logger);

        logger.LogInformation(
            "Generated a new DNSCrypt provider identity, persisted to '{Path}'.",
            path
        );

        return (providerKeyPair, entries);
    }

    /// <summary>
    ///     This method renames an unusable state file to <c>&lt;path&gt;.corrupt-&lt;UTC
    ///     timestamp&gt;</c> and returns the new path, or a note when the rename failed. A failed
    ///     rename is not fatal: the new identity then overwrites the file.
    /// </summary>
    private static string MoveAside(string path)
    {
        string asidePath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";

        try
        {
            File.Move(path, asidePath, overwrite: true);
            return asidePath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "could not be moved";
        }
    }

    /// <summary>
    ///     This method returns whether a stored certificate record has all its byte arrays, so a
    ///     null element or a record cut short is treated as a corrupt file, not an NRE later.
    /// </summary>
    private static bool IsComplete(PersistedCertificate persisted) =>
        persisted
            is {
                ResolverPublicKey: not null,
                ResolverSecretKey: not null,
                ClientMagic: not null,
                Signature: not null,
            };

    /// <summary>
    ///     This method writes the state file through <see cref="AtomicFile" />, which creates its
    ///     directory and gives the file owner-only access on Unix from the start.
    /// </summary>
    private static void WriteFile(
        string path,
        DnssecKeyPair providerKeyPair,
        IReadOnlyList<DnsCryptCertificateEntry> entries,
        ILogger logger
    )
    {
        try
        {
            PersistedState state = new(
                providerKeyPair.PublicKeyWireFormat,
                providerKeyPair.PrivateKeyPkcs8,
                [.. entries.Select(ToPersisted)]
            );

            AtomicFile.WriteAllBytes(
                path,
                Encoding.UTF8.GetBytes(JsonSerializer.SerializePretty(state)),
                AtomicFilePermissions.OwnerOnly
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The in-memory state stays usable for this process, so a failed write is only a
            // warning. The cost comes at the next restart, which starts from the old file or, if
            // there is none, a new provider identity that clients must be given again.
            logger.LogWarning(
                exception,
                "Failed to persist the DNSCrypt certificate state file at '{Path}'.",
                path
            );
        }
    }

    /// <summary>
    ///     This method converts a stored certificate record into a
    ///     <see cref="DnsCryptCertificateEntry" />.
    /// </summary>
    private static DnsCryptCertificateEntry ToEntry(PersistedCertificate persisted) =>
        new(
            new DnsCryptCertificate(
                persisted.EsVersion,
                persisted.ResolverPublicKey,
                persisted.ClientMagic,
                persisted.Serial,
                persisted.ValidFrom,
                persisted.ValidUntil,
                persisted.Signature
            ),
            persisted.ResolverSecretKey
        );

    /// <summary>
    ///     This method converts a <see cref="DnsCryptCertificateEntry" /> into its stored record.
    /// </summary>
    private static PersistedCertificate ToPersisted(DnsCryptCertificateEntry entry) =>
        new(
            entry.Certificate.EsVersion,
            entry.Certificate.ResolverPublicKey,
            entry.ResolverSecretKey,
            entry.Certificate.ClientMagic,
            entry.Certificate.Serial,
            entry.Certificate.ValidFrom,
            entry.Certificate.ValidUntil,
            entry.Certificate.Signature
        );

    /// <summary>
    ///     This record is the JSON shape of the state file.
    /// </summary>
    private sealed record PersistedState(
        byte[] ProviderPublicKey,
        byte[] ProviderPrivateKeyPkcs8,
        List<PersistedCertificate> Certificates
    );

    /// <summary>
    ///     This record is the JSON shape of one certificate in the state file.
    /// </summary>
    private sealed record PersistedCertificate(
        DnsCryptEsVersion EsVersion,
        byte[] ResolverPublicKey,
        byte[] ResolverSecretKey,
        byte[] ClientMagic,
        uint Serial,
        DateTimeOffset ValidFrom,
        DateTimeOffset ValidUntil,
        byte[] Signature
    );
}
