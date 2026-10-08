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
}
