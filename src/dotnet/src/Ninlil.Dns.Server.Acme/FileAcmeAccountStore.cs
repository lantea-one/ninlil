using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Common.Serializer;
using Ninlil.Dns.Common.IO;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class implements <see cref="IAcmeAccountStore" /> as one file at
///     <see cref="AcmeOptions.AccountStateFilePath" /> holding the JSON state encrypted with
///     <see cref="IDataProtector" /> (see that property for why it is encrypted). Like the RFC
///     5011 trust anchor store and the DNSCrypt certificate store, it loads the file or starts
///     empty (a corrupt file is moved aside first); here that happens on first use. Every change
///     rewrites the whole file atomically.
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
    ///     exist. A file that cannot be used (unreadable, not decryptable after the key ring was
    ///     lost, not JSON, or JSON without a domain table) is moved aside to
    ///     <c>&lt;path&gt;.corrupt-&lt;timestamp&gt;</c> and an Error is logged, so the next write
    ///     does not destroy an account key the operator could still recover. The service must
    ///     start either way, because ACME is optional.
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

                // A JSON "null" or an object without "Domains" would fail later, on first use.
                if (loaded?.Domains is not null)
                    return loaded;

                logger.LogError(
                    "The ACME account state file at '{Path}' has no account state; it was moved aside ({AsidePath}) and a fresh, empty state is used.",
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
                            or System.Security.Cryptography.CryptographicException
                )
            {
                logger.LogError(
                    exception,
                    "The ACME account state file at '{Path}' could not be read; it was moved aside ({AsidePath}) and a fresh, empty state is used.",
                    path,
                    MoveAside(path)
                );
            }
        }

        return new AcmeAccountState(null, []);
    }

    /// <summary>
    ///     This method renames an unusable state file to <c>&lt;path&gt;.corrupt-&lt;UTC
    ///     timestamp&gt;</c> and returns the new path, or a note when the rename failed. A failed
    ///     rename is not fatal: the fresh state then overwrites the file at the next write.
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
    ///     This method encrypts and writes the state file through <see cref="AtomicFile" />,
    ///     which creates its directory and gives the file owner-only access on Unix from the
    ///     start.
    /// </summary>
    private void WriteFile(AcmeAccountState state)
    {
        string path = options.Value.AccountStateFilePath;

        try
        {
            string encrypted = _protector.Protect(JsonSerializer.SerializePretty(state));
            AtomicFile.WriteAllBytes(
                path,
                Encoding.UTF8.GetBytes(encrypted),
                AtomicFilePermissions.OwnerOnly
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
