using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This hosted service keeps <see cref="IDnsCryptCertificateStore" /> supplied with a valid
///     certificate for every <see cref="DnsCryptOptions.EnabledEsVersions" /> cipher, without
///     operator action, like the automatic RFC 5011 trust-anchor rollover. It checks at startup
///     and every <see cref="DnsCryptOptions.CertificateRotationInterval" />, drops expired
///     certificates, and signs new ones with the store's provider key. It runs only while
///     DNSCrypt is enabled.
/// </summary>
/// <param name="store">The certificate store this service reads from and updates.</param>
/// <param name="options">
///     The DNSCrypt settings, read through <see cref="IOptionsMonitor{TOptions}" />. Enabling
///     DNSCrypt at runtime restarts this service together with <c>DnsCryptUdpListener</c> and
///     <c>DnsCryptTcpListener</c> (a listener without a certificate accepts queries but cannot
///     answer them), and the new <c>Enable</c> value is seen when <see cref="ExecuteAsync" />
///     runs again.
/// </param>
/// <param name="logger">The logger this service records new certificates and failed checks to.</param>
public sealed class DnsCryptCertificateRotationService(
    IDnsCryptCertificateStore store,
    IOptionsMonitor<DnsCryptOptions> options,
    ILogger<DnsCryptCertificateRotationService> logger
) : BackgroundService
{
    /// <summary>
    ///     This property reports whether the rotation loop is running. It is
    ///     <see langword="false" /> before <see cref="ExecuteAsync" /> runs, after it returns, and
    ///     when it returned at once because <c>DnsCryptOptions.Enable</c> was off. The generic host
    ///     calls <see cref="BackgroundService.StartAsync(CancellationToken)" /> at startup whatever
    ///     the <c>Enable</c> flag, so without this property the fallback start/stop bookkeeping of
    ///     <c>ManagedServiceRegistration</c> in <c>Server.Core</c> would report the service as
    ///     running on an installation where DNSCrypt is off. <c>TransportSettingsController</c>
    ///     starts the service only when <c>IsRunning</c> differs from the wanted state, so it would
    ///     then never start it when DNSCrypt is enabled at runtime, and the listeners would run
    ///     without a certificate.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    ///     This field is the longest wait before a failed certificate check is retried.
    /// </summary>
    private static readonly TimeSpan FailedCheckRetryInterval = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enable)
            return;

        IsRunning = true;

        try
        {
            TimeSpan delay = TimeSpan.Zero;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, stoppingToken);

                    EnsureCoverage();
                    delay = options.CurrentValue.CertificateRotationInterval;
                }
                catch (OperationCanceledException)
                {
                    // Expected during a graceful shutdown.
                }
                catch (Exception exception)
                {
                    // A failed check, including the first one at startup, must not stop the
                    // service or the host. The retry comes after the shorter of the rotation
                    // interval and FailedCheckRetryInterval, so a transient failure does not
                    // leave the listeners without a certificate for a whole interval.
                    delay = TimeSpan.FromTicks(
                        Math.Min(
                            options.CurrentValue.CertificateRotationInterval.Ticks,
                            FailedCheckRetryInterval.Ticks
                        )
                    );
                    logger.LogError(
                        exception,
                        "A DNSCrypt certificate rotation check failed unexpectedly; retrying in {RetryDelay}.",
                        delay
                    );
                }
            }
        }
        finally
        {
            // Reached on shutdown. The loop has stopped, so a later enable must be able to start
            // it again.
            IsRunning = false;
        }
    }

    /// <summary>
    ///     This method drops expired certificates, then, for each enabled cipher, generates a new
    ///     certificate unless one of that cipher's certificates is still valid at the next
    ///     scheduled check, so coverage never lapses between checks. It saves the result to the
    ///     store.
    /// </summary>
    private void EnsureCoverage()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<DnsCryptCertificateEntry> current =
        [
            .. store.Entries.Where(entry => !entry.Certificate.IsExpiredAt(now)),
        ];

        DateTimeOffset nextCheck = now + options.CurrentValue.CertificateRotationInterval;

        foreach (DnsCryptEsVersion esVersion in options.CurrentValue.EnabledEsVersions)
        {
            bool hasCoverageThroughNextCheck = current.Any(entry =>
                entry.Certificate.EsVersion == esVersion && entry.Certificate.ValidUntil > nextCheck
            );

            if (hasCoverageThroughNextCheck)
                continue;

            DnsCryptCertificateEntry newEntry = GenerateCertificate(esVersion, now, current);
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
    ///     This method generates and Ed25519-signs one new certificate with a new key pair, a
    ///     random client magic that no certificate in <paramref name="current" /> shares and that
    ///     <see cref="DnsCryptClientMagic" /> allows, and the current Unix time as its serial.
    /// </summary>
    private DnsCryptCertificateEntry GenerateCertificate(
        DnsCryptEsVersion esVersion,
        DateTimeOffset now,
        List<DnsCryptCertificateEntry> current
    )
    {
        (byte[] publicKey, byte[] secretKey) = DnsCryptCryptoBox.GenerateKeyPair(esVersion);
        byte[] clientMagic = DnsCryptClientMagic.Generate(
            current.Select(entry => entry.Certificate.ClientMagic)
        );
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
