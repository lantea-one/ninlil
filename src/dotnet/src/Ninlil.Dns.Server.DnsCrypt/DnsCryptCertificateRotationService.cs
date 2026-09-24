using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class keeps <see cref="IDnsCryptCertificateStore" /> continuously stocked with a
///     valid certificate for every <see cref="DnsCryptOptions.EnabledEsVersions" /> cipher, with
///     no operator action needed -- consistent with this project's own RFC 5011 automatic
///     trust-anchor rollover in automating exactly this kind of maintenance-burden task for its
///     homelab/home router deployment target, rather than requiring manual rotation the way
///     DNSSEC/TSIG key rollover deliberately still does.
/// </summary>
/// <param name="store">The certificate store this service reads from and updates.</param>
/// <param name="options">
///     This service's own configured rotation interval and validity duration -- read via
///     <see cref="IOptionsMonitor{TOptions}" /> so a REST-driven <c>enable</c> toggle of DNSCrypt as
///     a whole (which restarts this loop alongside <c>DnsCryptUdpListener</c>/
///     <c>DnsCryptTcpListener</c>, since a listener with no valid certificate to serve can
///     accept a connection but never actually answer one) is genuinely observed the next time
///     <see cref="ExecuteAsync" /> runs.
/// </param>
/// <param name="logger">The logger this service records every newly generated certificate to.</param>
public sealed class DnsCryptCertificateRotationService(
    IDnsCryptCertificateStore store,
    IOptionsMonitor<DnsCryptOptions> options,
    ILogger<DnsCryptCertificateRotationService> logger
) : BackgroundService
{
    /// <summary>
    ///     This property reports whether this service's own rotation loop is genuinely active
    ///     right now -- <see langword="false" /> both before <see cref="ExecuteAsync" /> has run
    ///     and for the entire lifetime of an invocation that exited immediately because
    ///     <c>DnsCryptOptions.Enable</c> was <see langword="false" /> at the time. Without a real
    ///     signal like this one, <c>Ninlil.Web.Services.ManagedServiceRegistration</c>'s
    ///     own fallback "has StartAsync been called more recently than StopAsync" bookkeeping
    ///     would report this service as already running the instant the process starts -- true of
    ///     every hosted service in this process, since the generic host always calls
    ///     <see cref="BackgroundService.StartAsync(CancellationToken)" /> once at boot regardless
    ///     of any <c>Enable</c> flag -- even on a fresh installation where DNSCrypt (and therefore
    ///     this loop) starts disabled. <c>Ninlil.Web.Controllers.TransportSettingsController</c>'s
    ///     own live <c>enable</c> toggle only calls this registration's own
    ///     <c>IManagedServiceRegistration.StartAsync</c> when <c>IsRunning</c> actually disagrees
    ///     with the desired state (see that controller's own doc comment on why a redundant call
    ///     would be unsafe) -- with the stale bookkeeping fallback, that guard would silently skip
    ///     ever restarting this loop, so a DNSCrypt transport enabled live on a fresh install would
    ///     genuinely bind its socket (the two listeners each have their own real <c>IsListening</c>
    ///     probe already) but never actually issue a certificate, leaving it deaf to every real,
    ///     encrypted client query -- exactly the failure mode this class's own constructor
    ///     parameter doc comment already warns a missing certificate causes.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enable)
            return;

        IsRunning = true;

        try
        {
            EnsureCoverage();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(
                        options.CurrentValue.CertificateRotationInterval,
                        stoppingToken
                    );
                    EnsureCoverage();
                }
                catch (OperationCanceledException)
                {
                    // Expected during a graceful shutdown.
                }
                catch (Exception exception)
                {
                    // A single failed rotation attempt must never take this whole background
                    // service down -- there is always another attempt, at worst
                    // CertificateRotationInterval later, and any certificate already issued stays
                    // valid regardless.
                    logger.LogWarning(
                        exception,
                        "A DNSCrypt certificate rotation check failed unexpectedly."
                    );
                }
            }
        }
        finally
        {
            // Reached both on a graceful shutdown and (harmlessly) if EnsureCoverage's own
            // initial call above throws -- either way, this loop is no longer genuinely active,
            // so a later live re-enable must be free to actually restart it rather than being
            // told (incorrectly) that it already is.
            IsRunning = false;
        }
    }

    /// <summary>
    ///     This method prunes any certificate that has already expired, then, for every
    ///     enabled cipher, generates a fresh certificate if none of that cipher's currently
    ///     tracked certificates will still be valid by the next scheduled check -- guaranteeing
    ///     continuous coverage rather than reacting only after a gap has already opened.
    /// </summary>
    private void EnsureCoverage()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<DnsCryptCertificateEntry> current =
        [
            .. store.Entries.Where(entry => entry.Certificate.ValidUntil > now),
        ];

        DateTimeOffset nextCheck = now + options.CurrentValue.CertificateRotationInterval;

        foreach (DnsCryptEsVersion esVersion in options.CurrentValue.EnabledEsVersions)
        {
            bool hasCoverageThroughNextCheck = current.Any(entry =>
                entry.Certificate.EsVersion == esVersion && entry.Certificate.ValidUntil > nextCheck
            );

            if (hasCoverageThroughNextCheck)
                continue;

            DnsCryptCertificateEntry newEntry = GenerateCertificate(esVersion, now);
            current.Add(newEntry);

            logger.LogInformation(
                "Generated a new DNSCrypt certificate (cipher {EsVersion}, serial {Serial}, valid {ValidFrom} to {ValidUntil}).",
                esVersion,
                newEntry.Certificate.Serial,
                newEntry.Certificate.ValidFrom,
                newEntry.Certificate.ValidUntil
            );
        }

        store.Save(current);
    }

    /// <summary>
    ///     This method generates, and Ed25519-signs, a single new certificate.
    /// </summary>
    private DnsCryptCertificateEntry GenerateCertificate(
        DnsCryptEsVersion esVersion,
        DateTimeOffset now
    )
    {
        (byte[] publicKey, byte[] secretKey) = DnsCryptCryptoBox.GenerateKeyPair(esVersion);
        byte[] clientMagic = RandomNumberGenerator.GetBytes(8);
        uint serial = (uint)now.ToUnixTimeSeconds();

        DnsCryptCertificate unsigned = new(
            esVersion,
            publicKey,
            clientMagic,
            serial,
            now - options.CurrentValue.CertificateClockSkewTolerance,
            now + options.CurrentValue.CertificateValidityDuration,
            []
        );

        if (
            !DnssecAlgorithmRegistry.TryGetAlgorithm(
                DnssecAlgorithmEnum.Ed25519,
                out IDnssecSigningAlgorithm algorithm
            )
        )
            throw new InvalidOperationException(
                "The Ed25519 signing algorithm, required for DNSCrypt's own certificate signing, is not registered."
            );

        byte[] signature = algorithm.Sign(
            DnsCryptCertificateCodec.BuildSignedData(unsigned),
            store.ProviderKeyPair.PrivateKeyPkcs8
        );

        return new DnsCryptCertificateEntry(unsigned with { Signature = signature }, secretKey);
    }
}
