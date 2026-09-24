using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Certificates;

/// <summary>
///     This class wraps <see cref="TlsCertificateLoader" /> with a periodic re-check of the
///     configured certificate file's own last-write time, so an already-running TLS-based
///     transport picks up a certificate replaced on disk after startup -- most notably, one
///     <c>Ninlil.Dns.Server.Acme</c> renews mid-run -- without needing a restart, matching
///     this project's own "an operator should never have to remember to do X, or it silently
///     degrades over time" deployment-target principle already applied to RFC 5011's automated
///     trust anchor rollover.
///     <para>
///         A stale, already-superseded <see cref="X509Certificate2" /> is deliberately never
///         disposed once replaced: a TLS handshake already in flight against it must be able to
///         finish using it safely, and its own finalizer reclaims it once nothing references it
///         anymore.
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
    ///     describes immediately, then starts a background timer that re-checks it every
    ///     <paramref name="pollInterval" />.
    /// </summary>
    /// <param name="options">Where to load an existing certificate from, or how to generate one.</param>
    /// <param name="logger">The logger this provider records a reload, or a failed reload check, to.</param>
    /// <param name="pollInterval">How often to re-check the certificate's own source file for a change. Defaults to 5 minutes.</param>
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
    ///     This property contains the currently active certificate. A caller must read this
    ///     fresh on every use (a per-connection TLS certificate-selection callback, never a value
    ///     cached once at startup) for a reload to actually take effect without a restart.
    /// </summary>
    public X509Certificate2 Current => Volatile.Read(ref _certificate);

    /// <summary>
    ///     This method re-checks whether the configured certificate's own source file has
    ///     changed since this provider last loaded it, reloading if so.
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
            // A failed reload check must never take down whatever transport owns this provider
            // -- the previously loaded certificate stays in Current and remains perfectly
            // usable; at worst, this simply retries at the next poll.
            _logger.LogWarning(
                exception,
                "Failed to check for a replaced TLS certificate; continuing with the current one."
            );
        }
    }

    /// <summary>
    ///     This method reads the last-write time of whichever file <see cref="Current" /> was
    ///     actually loaded from -- an operator-provided PFX, or the self-signed state file.
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
