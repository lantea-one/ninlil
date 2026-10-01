using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class implements <see cref="IAcmeAccountStore" /> as one file at
///     <see cref="AcmeOptions.AccountStateFilePath" /> holding the JSON state encrypted with
///     <see cref="IDataProtector" /> (see that property for why it is encrypted). Like the RFC
///     5011 trust anchor store and the DNSCrypt certificate store, it loads the file or starts
///     empty; here that happens on first use. Every change rewrites the whole file.
/// </summary>
/// <param name="dataProtectionProvider">The provider the store's purpose-scoped protector is created from.</param>
/// <param name="options">The ACME settings, read for the state file path.</param>
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
    ///     This method loads the state file, or returns an empty state when the file does not
    ///     exist or fails with an I/O, format or decryption error (for example after the key ring
    ///     was lost). The next write then replaces the old file. Malformed JSON is not caught.
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
    ///     This method encrypts and writes the state file, creating its directory if needed, and
    ///     restricts it to owner read/write on Unix.
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
            // The in-memory state stays usable for this process, so a failed write is only a
            // warning; the change is lost at the next restart.
            logger.LogWarning(
                exception,
                "Failed to persist the ACME account state file at '{Path}'.",
                path
            );
        }
    }
}
