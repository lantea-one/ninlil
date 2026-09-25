using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Certificates;

/// <summary>
///     This class wraps <see cref="TlsCertificateLoader" /> and re-checks the certificate file's
///     last-write time on a timer, so a running TLS transport picks up a certificate replaced on
///     disk (for example one <c>Ninlil.Dns.Server.Acme</c> renews) without a restart.
///     <para>
///         A replaced <see cref="X509Certificate2" /> is not disposed, because a handshake in
///         progress may still be using it; the garbage collector reclaims it.
///     </para>
/// </summary>
public sealed class ReloadableTlsCertificateProvider : IDisposable
{
    private readonly TlsCertificateOptions _options;
    private readonly ILogger _logger;
    private readonly Timer _timer;
    private DateTime _lastObservedWriteTimeUtc;
    private X509Certificate2 _certificate;

    /// <summary>
    ///     This constructor loads (or generates) the certificate <paramref name="options" />
    ///     describes, then starts a timer that re-checks it every <paramref name="pollInterval" />.
    /// </summary>
    /// <param name="options">Where to load an existing certificate from, or how to generate one.</param>
    /// <param name="logger">The logger this provider records a reload, or a failed reload check, to.</param>
    /// <param name="pollInterval">How often to re-check the certificate file for a change. Defaults to 5 minutes.</param>
    public ReloadableTlsCertificateProvider(
        TlsCertificateOptions options,
        ILogger logger,
        TimeSpan? pollInterval = null
    )
    {
        _options = options;
        _logger = logger;
        _certificate = TlsCertificateLoader.LoadOrGenerate(options, logger);
        _lastObservedWriteTimeUtc = ReadSourceFileWriteTimeUtc();

        TimeSpan interval = pollInterval ?? TimeSpan.FromMinutes(5);
        _timer = new Timer(_ => ReloadIfChanged(), null, interval, interval);
    }

    /// <summary>
    ///     This property contains the current certificate. Read it on every use (for example in a
    ///     per-connection certificate selection callback), not once at startup, or a reload has
    ///     no effect.
    /// </summary>
    public X509Certificate2 Current => Volatile.Read(ref _certificate);

    /// <summary>
    ///     This method reloads the certificate if its file is newer than the last one loaded.
    /// </summary>
    private void ReloadIfChanged()
    {
        try
        {
            DateTime writeTimeUtc = ReadSourceFileWriteTimeUtc();

            if (writeTimeUtc <= _lastObservedWriteTimeUtc)
                return;

            X509Certificate2 reloaded = TlsCertificateLoader.LoadOrGenerate(_options, _logger);
            Volatile.Write(ref _certificate, reloaded);
            _lastObservedWriteTimeUtc = writeTimeUtc;

            _logger.LogInformation(
                "Reloaded a TLS certificate replaced on disk since this transport started."
            );
        }
        catch (Exception exception)
        {
            // Keep the current certificate and try again at the next poll; the write time is
            // not recorded, so a half-written file is retried.
            _logger.LogWarning(
                exception,
                "Failed to check for a replaced TLS certificate; continuing with the current one."
            );
        }
    }

    /// <summary>
    ///     This method reads the last-write time of the file <see cref="Current" /> comes from:
    ///     the configured PFX, or the self-signed state file.
    /// </summary>
    private DateTime ReadSourceFileWriteTimeUtc()
    {
        string path = string.IsNullOrWhiteSpace(_options.PfxPath)
            ? _options.SelfSignedCertificateStatePath
            : _options.PfxPath;

        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }

    /// <inheritdoc />
    public void Dispose() => _timer.Dispose();
}
