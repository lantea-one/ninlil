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
///     not need the database. The state is loaded (or a new identity generated, after moving a
///     damaged file aside) when the singleton is constructed, and <see cref="Save" /> rewrites
///     the whole file atomically.
/// </summary>
/// <param name="options">The DNSCrypt settings, read for the state file path.</param>
/// <param name="logger">The logger this store records a corrupt or unreadable state file to.</param>
public sealed class FileDnsCryptCertificateStore(
    IOptions<DnsCryptOptions> options,
    ILogger<FileDnsCryptCertificateStore> logger
) : IDnsCryptCertificateStore
{
    private readonly Lock _lock = new();
    private readonly (
        DnssecKeyPair ProviderKeyPair,
        List<DnsCryptCertificateEntry> Entries
    ) _state = LoadOrSeed(options.Value, logger);

    /// <inheritdoc />
    public DnssecKeyPair ProviderKeyPair => _state.ProviderKeyPair;

    /// <inheritdoc />
    public IReadOnlyList<DnsCryptCertificateEntry> Entries
    {
        get
        {
            lock (_lock)
                return [.. _state.Entries];
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<DnsCryptCertificateEntry> entries)
    {
        lock (_lock)
        {
            _state.Entries.Clear();
            _state.Entries.AddRange(entries);
            WriteFile(
                options.Value.CertificateStateFilePath,
                _state.ProviderKeyPair,
                _state.Entries,
                logger
            );
        }
    }

    /// <summary>
    ///     This method loads the state file, or creates a new one with a newly generated provider
    ///     key pair and no certificates when the file does not exist or is unusable (unreadable,
    ///     not JSON, or missing the provider keys or the certificate list). An unusable file is
    ///     moved aside to <c>&lt;path&gt;.corrupt-&lt;timestamp&gt;</c> and an Error is logged.
    ///     This runs when the singleton is constructed, whether or not DNSCrypt is enabled, so it
    ///     must not throw for a damaged file or the host would not start.
    /// </summary>
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
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or FormatException
                            or System.Text.Json.JsonException
                )
            {
                logger.LogError(
                    exception,
                    "The DNSCrypt certificate state file at '{Path}' could not be read; it was moved aside ({AsidePath}) and a new provider identity is generated. Clients need the new stamp.",
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
