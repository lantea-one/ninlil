using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class implements <see cref="IAcmeAccountStore" /> as a single JSON file on disk,
///     encrypted at rest via <see cref="IDataProtector" /> -- the load-or-seed convention this
///     project already uses for the RFC 5011 trust anchor store and DNSCrypt's own certificate
///     store, plus the same encrypted-JSON-blob-on-disk shape <c>Server.DnsCrypt</c> does not
///     need but this class does, since an ACME account key is genuinely security-sensitive (see
///     the reasoning on <see cref="AcmeOptions.AccountStateFilePath" />).
/// </summary>
/// <param name="dataProtectionProvider">The provider this store derives its own purpose-scoped key from.</param>
/// <param name="options">This store's own configured file path.</param>
/// <param name="logger">The logger this store records a corrupt or unreadable state file to.</param>
public sealed class FileAcmeAccountStore(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<AcmeOptions> options,
    ILogger<FileAcmeAccountStore> logger
) : IAcmeAccountStore
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Ninlil.Dns.Server.Acme.AccountState"
    );
    private readonly Lock _lock = new();
    private AcmeAccountState _state;

    /// <inheritdoc />
    public string AccountKeyPem
    {
        get
        {
            lock (_lock)
                return (_state ??= LoadOrSeed()).AccountKeyPem;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, AcmeDomainCertificateState> DomainStates
    {
        get
        {
            lock (_lock)
                return new Dictionary<string, AcmeDomainCertificateState>(
                    (_state ??= LoadOrSeed()).Domains
                );
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, AcmeDomainRenewalFailure> Failures
    {
        get
        {
            lock (_lock)
                return new Dictionary<string, AcmeDomainRenewalFailure>(
                    (_state ??= LoadOrSeed()).Failures
                );
        }
    }

    /// <inheritdoc />
    public void SaveAccountKey(string accountKeyPem)
    {
        lock (_lock)
        {
            _state = (_state ??= LoadOrSeed()) with { AccountKeyPem = accountKeyPem };
            WriteFile(_state);
        }
    }

    /// <inheritdoc />
    public void SaveDomainState(string domain, AcmeDomainCertificateState state)
    {
        lock (_lock)
        {
            _state ??= LoadOrSeed();
            _state.Domains[domain] = state;
            WriteFile(_state);
        }
    }

    /// <inheritdoc />
    public void SaveDomainFailure(string domain, AcmeDomainRenewalFailure failure)
    {
        lock (_lock)
        {
            _state ??= LoadOrSeed();
            _state.Failures[domain] = failure;
            WriteFile(_state);
        }
    }

    /// <inheritdoc />
    public void ClearDomainFailure(string domain)
    {
        lock (_lock)
        {
            _state ??= LoadOrSeed();

            if (_state.Failures.Remove(domain))
                WriteFile(_state);
        }
    }

    /// <summary>
    ///     This method loads this store's state file, or seeds a brand-new, empty one if the
    ///     file does not exist yet, or cannot be read at all.
    /// </summary>
    private AcmeAccountState LoadOrSeed()
    {
        string path = options.Value.AccountStateFilePath;

        if (File.Exists(path))
        {
            try
            {
                string decrypted = _protector.Unprotect(File.ReadAllText(path));
                AcmeAccountState loaded = JsonSerializer.Deserialize<AcmeAccountState>(decrypted);

                if (loaded is not null)
                    return loaded;
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or FormatException
                            or System.Security.Cryptography.CryptographicException
                )
            {
                logger.LogWarning(
                    exception,
                    "The ACME account state file at '{Path}' could not be read; starting with a fresh, empty state.",
                    path
                );
            }
        }

        return new AcmeAccountState(null, []);
    }

    /// <summary>
    ///     This method persists this store's state to disk, creating its containing directory
    ///     if it does not exist yet.
    /// </summary>
    private void WriteFile(AcmeAccountState state)
    {
        string path = options.Value.AccountStateFilePath;

        try
        {
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string encrypted = _protector.Protect(JsonSerializer.SerializePretty(state));
            File.WriteAllText(path, encrypted);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException exception)
        {
            // A failed write leaves the in-memory state (already updated by the caller) usable
            // for the rest of this process's lifetime; only a future restart would lose it,
            // which is an availability concern, not a security or correctness one -- a warning,
            // not fatal.
            logger.LogWarning(
                exception,
                "Failed to persist the ACME account state file at '{Path}'.",
                path
            );
        }
    }
}
