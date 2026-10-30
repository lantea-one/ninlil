using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.DnsCrypt.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests;

/// <summary>
///     This class tests <see cref="DnsCryptCertificateRotationService" /> by starting it against
///     a <see cref="FileDnsCryptCertificateStore" /> in a temporary file, with short intervals:
///     it issues one certificate per enabled cipher at startup and new ones on later ticks.
/// </summary>
public class DnsCryptCertificateRotationServiceTests
{
    [Fact]
    public async Task StartAsync_IssuesOneCertificatePerEnabledCipher()
    {
        string statePath = Path.Combine(
            Path.GetTempPath(),
            $"dnscrypt-rotation-test-{Guid.NewGuid():N}.json"
        );

        try
        {
            DnsCryptOptions options = new()
            {
                Enable = true,
                CertificateStateFilePath = statePath,
                EnabledEsVersions =
                [
                    DnsCryptEsVersion.XChaCha20Poly1305,
                    DnsCryptEsVersion.XSalsa20Poly1305,
                ],
                CertificateRotationInterval = TimeSpan.FromMinutes(10),
                CertificateValidityDuration = TimeSpan.FromMinutes(20),
                CertificateClockSkewTolerance = TimeSpan.Zero,
            };

            FileDnsCryptCertificateStore store = new(
                Options.Create(options),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            DnsCryptCertificateRotationService service = new(
                store,
                new StaticOptionsMonitor<DnsCryptOptions>(options),
                NullLogger<DnsCryptCertificateRotationService>.Instance
            );

            await service.StartAsync(CancellationToken.None);

            for (int attempt = 0; attempt < 40 && store.Entries.Count < 2; attempt++)
                await Task.Delay(50);

            Assert.Equal(2, store.Entries.Count);
            Assert.Equal(
                2,
                store.Entries.Select(entry => entry.Certificate.EsVersion).Distinct().Count()
            );

            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }

    [Fact]
    public async Task WithDnsCryptDisabled_RunningTheService_WritesNoStateFile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dnscrypt-rotation-{Guid.NewGuid():N}"
        );
        string statePath = Path.Combine(directory, "state.json");

        try
        {
            DnsCryptOptions options = new()
            {
                Enable = false,
                CertificateStateFilePath = statePath,
            };
            FileDnsCryptCertificateStore store = new(
                Options.Create(options),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            DnsCryptCertificateRotationService service = new(
                store,
                new StaticOptionsMonitor<DnsCryptOptions>(options),
                NullLogger<DnsCryptCertificateRotationService>.Instance
            );

            await service.StartAsync(CancellationToken.None);
            await service.ExecuteTask;
            await service.StopAsync(CancellationToken.None);

            Assert.False(File.Exists(statePath));
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ARealRotationIntervalTick_IssuesFreshCertificatesBeyondTheInitialSet()
    {
        string statePath = Path.Combine(
            Path.GetTempPath(),
            $"dnscrypt-rotation-tick-test-{Guid.NewGuid():N}.json"
        );

        try
        {
            DnsCryptOptions options = new()
            {
                Enable = true,
                CertificateStateFilePath = statePath,
                EnabledEsVersions = [DnsCryptEsVersion.XChaCha20Poly1305],
                CertificateRotationInterval = TimeSpan.FromSeconds(1),
                CertificateValidityDuration = TimeSpan.FromSeconds(2),
                CertificateClockSkewTolerance = TimeSpan.Zero,
            };

            FileDnsCryptCertificateStore store = new(
                Options.Create(options),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            DnsCryptCertificateRotationService service = new(
                store,
                new StaticOptionsMonitor<DnsCryptOptions>(options),
                NullLogger<DnsCryptCertificateRotationService>.Instance
            );

            await service.StartAsync(CancellationToken.None);

            for (int attempt = 0; attempt < 40 && store.Entries.Count < 1; attempt++)
                await Task.Delay(50);

            uint[] initialSerials = [.. store.Entries.Select(entry => entry.Certificate.Serial)];

            await Task.Delay(TimeSpan.FromSeconds(3));

            uint[] serialsNow =
            [
                .. store.Entries.Select(entry => entry.Certificate.Serial).Distinct(),
            ];

            Assert.True(serialsNow.Except(initialSerials).Any());

            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }

    [Fact]
    public async Task AFailedFirstCheck_DoesNotStopTheServiceAndIsRetried()
    {
        string statePath = Path.Combine(
            Path.GetTempPath(),
            $"dnscrypt-rotation-retry-test-{Guid.NewGuid():N}.json"
        );

        try
        {
            DnsCryptOptions options = new()
            {
                Enable = true,
                CertificateStateFilePath = statePath,
                EnabledEsVersions = [DnsCryptEsVersion.XChaCha20Poly1305],
                // The retry delay is the smaller of this and five minutes.
                CertificateRotationInterval = TimeSpan.FromMilliseconds(200),
                CertificateValidityDuration = TimeSpan.FromMinutes(20),
                CertificateClockSkewTolerance = TimeSpan.Zero,
            };

            FileDnsCryptCertificateStore inner = new(
                Options.Create(options),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            FailingFirstReadCertificateStore store = new(inner, failuresBeforeSuccess: 1);
            DnsCryptCertificateRotationService service = new(
                store,
                new StaticOptionsMonitor<DnsCryptOptions>(options),
                NullLogger<DnsCryptCertificateRotationService>.Instance
            );

            await service.StartAsync(CancellationToken.None);

            for (int attempt = 0; attempt < 60 && inner.Entries.Count < 1; attempt++)
                await Task.Delay(50);

            Assert.Single(inner.Entries);
            Assert.True(service.IsRunning);
            Assert.False(service.ExecuteTask.IsCompleted);

            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }

    [Fact]
    public void DefaultOptions_ACertificateOutlivesTheRotationIntervalByFourHours()
    {
        // DNSCrypt v2 section 13: rotate at least every 24 hours, accept the old key for at least
        // 4 hours, then throw it away. A 28 hour validity after a 24 hour interval does both.
        DnsCryptOptions options = new();

        Assert.Equal(TimeSpan.FromHours(24), options.CertificateRotationInterval);
        Assert.Equal(TimeSpan.FromHours(28), options.CertificateValidityDuration);
    }

    [Fact]
    public async Task ACertificateThatExpiresBetweenTwoRotationChecks_IsDroppedWhenItExpires()
    {
        string statePath = Path.Combine(
            Path.GetTempPath(),
            $"dnscrypt-rotation-expiry-test-{Guid.NewGuid():N}.json"
        );

        try
        {
            // A one hour interval means the next rotation check is far away; only a wake-up at
            // the old certificate's end time can drop it within the test.
            DnsCryptOptions options = new()
            {
                Enable = true,
                CertificateStateFilePath = statePath,
                EnabledEsVersions = [DnsCryptEsVersion.XChaCha20Poly1305],
                CertificateRotationInterval = TimeSpan.FromHours(1),
                CertificateValidityDuration = TimeSpan.FromHours(2),
                CertificateClockSkewTolerance = TimeSpan.Zero,
            };

            FileDnsCryptCertificateStore store = new(
                Options.Create(options),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            (byte[] publicKey, byte[] secretKey) = DnsCryptCryptoBox.GenerateKeyPair(
                DnsCryptEsVersion.XSalsa20Poly1305
            );
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DnsCryptCertificateEntry oldEntry = new(
                new DnsCryptCertificate(
                    DnsCryptEsVersion.XSalsa20Poly1305,
                    publicKey,
                    [1, 2, 3, 4, 5, 6, 7, 8],
                    1,
                    now.AddHours(-1),
                    now.AddSeconds(2),
                    new byte[64]
                ),
                secretKey
            );
            store.Save([oldEntry]);

            DnsCryptCertificateRotationService service = new(
                store,
                new StaticOptionsMonitor<DnsCryptOptions>(options),
                NullLogger<DnsCryptCertificateRotationService>.Instance
            );

            await service.StartAsync(CancellationToken.None);

            // The old certificate is the only entry until the startup check runs, so wait for
            // the new certificate first, then for the old one to go.
            for (
                int attempt = 0;
                attempt < 100
                    && !(
                        store.Entries.Any(entry =>
                            entry.Certificate.EsVersion == DnsCryptEsVersion.XChaCha20Poly1305
                        )
                        && store.Entries.All(entry =>
                            entry.Certificate.EsVersion != DnsCryptEsVersion.XSalsa20Poly1305
                        )
                    );
                attempt++
            )
                await Task.Delay(100);

            // The startup check keeps the old certificate (it has not expired yet) and issues
            // the enabled cipher's certificate; the wake-up after the old end time drops it.
            Assert.Single(store.Entries);
            Assert.Equal(
                DnsCryptEsVersion.XChaCha20Poly1305,
                store.Entries[0].Certificate.EsVersion
            );

            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }
}
