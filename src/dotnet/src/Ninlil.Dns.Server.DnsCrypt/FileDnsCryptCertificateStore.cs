using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Common.Serializer;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class implements <see cref="IDnsCryptCertificateStore" /> as a single JSON file on
///     disk, following the same "small local file for state that genuinely cannot live in the
///     database" convention already used for the RFC 5011 trust anchor store and the
///     self-signed TLS certificate -- a full database dependency would be disproportionate for
///     tracking a resolver's own automatically generated, operator-invisible DNSCrypt identity.
/// </summary>
/// <param name="options">This store's own configured file path.</param>
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
    ///     This method loads this store's state file, or seeds a brand-new one -- a freshly
    ///     generated provider key pair and no certificates yet -- if the file does not exist
    ///     yet, or cannot be read at all.
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
    ///     This method persists this store's state to disk, creating its containing directory
    ///     if it does not exist yet.
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
            // A failed write leaves the in-memory state (already updated by the caller) usable
            // for the rest of this process's lifetime; only a *future* restart would regenerate
            // a new provider identity instead of reusing this one, which is an availability/UX
            // concern, not a security or correctness one -- a warning, not fatal.
            logger.LogWarning(
                exception,
                "Failed to persist the DNSCrypt certificate state file at '{Path}'.",
                path
            );
        }
    }

    /// <summary>
    ///     This method converts a persisted certificate record back into a live
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
    ///     This method converts a live <see cref="DnsCryptCertificateEntry" /> into its
    ///     on-disk persisted record shape.
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
    ///     This class carries this store's own on-disk JSON shape.
    /// </summary>
    private sealed record PersistedState(
        byte[] ProviderPublicKey,
        byte[] ProviderPrivateKeyPkcs8,
        List<PersistedCertificate> Certificates
    );

    /// <summary>
    ///     This class carries a single certificate's own on-disk JSON shape.
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
