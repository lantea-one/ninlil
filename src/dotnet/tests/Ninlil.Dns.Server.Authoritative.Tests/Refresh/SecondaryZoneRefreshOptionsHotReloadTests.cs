using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Refresh;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Presets;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Refresh;

/// <summary>
///     This class checks that changes to <see cref="SecondaryZoneRefreshOptions" /> written through
///     <see cref="PresetConfigurationFileApplier" /> (what the secondary zone refresh settings
///     controller calls) into a reload-on-change JSON file reach <see cref="SecondaryZoneRefreshService" />
///     without a restart. It builds a SQLite host that binds the options section to the file, with
///     a <see cref="ManualTimeProvider" /> and a transfer client whose primary never answers; the
///     host is not started, so the refresh loop never runs and the test drives each pass itself
///     through <c>CheckDueZonesAsync</c>. The poll interval is read by the loop on every
///     iteration, which this class checks through the monitor the service holds.
/// </summary>
public sealed class SecondaryZoneRefreshOptionsHotReloadTests
{
    /// <summary>
    ///     This class is a transfer client for the Plain transport whose primary is unreachable:
    ///     no SOA answer and a failed pull.
    /// </summary>
    private sealed class UnreachablePrimaryClient : IZoneTransferClient
    {
        /// <inheritdoc />
        public ZoneTransferTransportEnum Transport => ZoneTransferTransportEnum.Plain;

        /// <inheritdoc />
        public Task<uint?> QuerySoaSerialAsync(
            System.Net.IPAddress primaryAddress,
            DnsName zoneName,
            string tsigKeyName,
            CancellationToken stoppingToken = default
        ) => Task.FromResult<uint?>(null);

        /// <inheritdoc />
        public Task<ZoneTransferPullResult> PullAsync(
            System.Net.IPAddress primaryAddress,
            DnsName zoneName,
            uint? currentSerial,
            string tsigKeyName,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(ZoneTransferPullResult.Failure("The primary is unreachable."));
    }

    [Fact]
    public async Task AWriteOfDefaultRetryInterval_ChangesTheNextCheckOfAZoneWithNoSoa_WithNoRestart()
    {
        // A directory for this test rather than the shared temp directory: the file watcher
        // behind reloadOnChange sees every change in the directory it watches, and unrelated
        // churn there can make it report a change to every watched file.
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-secondary-refresh-hot-reload-")
            .FullName;
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            ManualTimeProvider clock = new();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            await using AuthoritativeTestZoneHost host =
                await AuthoritativeTestZoneHost.CreateAsync(configureServices: services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(clock);
                    services.RemoveAll<IZoneTransferClient>();
                    services.AddSingleton<IZoneTransferClient, UnreachablePrimaryClient>();

                    // Layered on the host's own binding: only the keys present in the file
                    // override the defaults.
                    services.Configure<SecondaryZoneRefreshOptions>(
                        configuration.GetSection(SecondaryZoneRefreshOptions.SectionName)
                    );
                });
            Guid zoneId = await SeedSecondaryZoneWithNoSoaAsync(host);

            SecondaryZoneRefreshService service =
                host.Services.GetRequiredService<SecondaryZoneRefreshService>();
            IOptionsMonitor<SecondaryZoneRefreshOptions> monitor = host.Services.GetRequiredService<
                IOptionsMonitor<SecondaryZoneRefreshOptions>
            >();

            // The compiled-in default: a zone with no SOA is retried after 30 seconds.
            DateTimeOffset firstPassAt = clock.GetUtcNow();
            await service.CheckDueZonesAsync(CancellationToken.None);
            Assert.Equal(firstPassAt + TimeSpan.FromSeconds(30), service.GetNextCheckAtUtc(zoneId));

            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        SecondaryZoneRefreshOptions.SectionName,
                        nameof(SecondaryZoneRefreshOptions.DefaultRetryInterval),
                        "00:02:00"
                    ),
                    new PresetConfigurationSetting(
                        SecondaryZoneRefreshOptions.SectionName,
                        nameof(SecondaryZoneRefreshOptions.PollInterval),
                        "00:00:07"
                    ),
                ],
                path
            );

            // The JSON provider debounces file-change notifications. Wait up to ten seconds for
            // the written values themselves, not for a notification or a fixed delay: a spurious
            // notification can reload the file before the write lands.
            for (
                int attempt = 0;
                attempt < 200
                    && (
                        monitor.CurrentValue.DefaultRetryInterval != TimeSpan.FromMinutes(2)
                        || monitor.CurrentValue.PollInterval != TimeSpan.FromSeconds(7)
                    );
                attempt++
            )
                await Task.Delay(50);

            // The loop reads PollInterval from the same monitor on every iteration.
            Assert.Equal(TimeSpan.FromSeconds(7), monitor.CurrentValue.PollInterval);
            Assert.Equal(TimeSpan.FromMinutes(2), monitor.CurrentValue.DefaultRetryInterval);

            // Move past the first retry so the zone is due, then check it once more.
            clock.Advance(TimeSpan.FromSeconds(31));
            DateTimeOffset secondPassAt = clock.GetUtcNow();
            await service.CheckDueZonesAsync(CancellationToken.None);
            Assert.Equal(secondPassAt + TimeSpan.FromMinutes(2), service.GetNextCheckAtUtc(zoneId));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover temporary directory is harmless.
            }
        }
    }

    /// <summary>
    ///     This method stores a Secondary zone that has no records, so no local SOA, and one
    ///     primary address. The host is not started, so no cache needs to load it.
    /// </summary>
    /// <returns>The identifier of the new zone.</returns>
    private static async Task<Guid> SeedSecondaryZoneWithNoSoaAsync(AuthoritativeTestZoneHost host)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        Guid zoneId = Guid.NewGuid();

        await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .AddAsync(
                new ZoneEntity
                {
                    Id = zoneId,
                    Name = "hot-reload.test",
                    Type = ZoneTypeEnum.Secondary,
                    PrimaryServerAddresses = ["127.0.0.1"],
                    TransferTransport = ZoneTransferTransportEnum.Plain,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return zoneId;
    }
}
