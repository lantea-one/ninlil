using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Common.Serializer;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class implements <see cref="IDnsCryptCertificateStore" /> as one JSON file at
///     <see cref="DnsCryptOptions.CertificateStateFilePath" />, like the RFC 5011 trust anchor
///     store and the self-signed TLS certificate: a small, automatically generated identity does
///     not need the database. The state is loaded (or a new identity generated) when the
///     singleton is constructed, and <see cref="Save" /> rewrites the whole file.
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
    ///     key pair and no certificates when the file does not exist or fails with an I/O or
    ///     format error. Other errors, such as malformed JSON, are not caught.
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

                if (loaded is not null)
                    return (
                        new DnssecKeyPair(loaded.ProviderPublicKey, loaded.ProviderPrivateKeyPkcs8),
                        [.. loaded.Certificates.Select(ToEntry)]
                    );
            }
            catch (Exception exception) when (exception is IOException or FormatException)
            {
                logger.LogWarning(
                    exception,
                    "The DNSCrypt certificate state file at '{Path}' could not be read; generating a new provider identity.",
                    path
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
    ///     This method writes the state file, creating its directory if needed, and restricts it
    ///     to owner read/write on Unix after writing.
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
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            PersistedState state = new(
                providerKeyPair.PublicKeyWireFormat,
                providerKeyPair.PrivateKeyPkcs8,
                [.. entries.Select(ToPersisted)]
            );

            File.WriteAllText(path, JsonSerializer.SerializePretty(state));

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException exception)
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
