using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Tests;

/// <summary>
///     This class proves, against the real .NET configuration/options binding pipeline and a real
///     reload-on-change JSON configuration file -- not merely
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read in isolation -- that a write
///     reaching <see cref="DnssecAlgorithmPolicyOptions" /> via
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (the exact mechanism
///     <c>Ninlil.Web.Controllers.DnssecPolicySettingsController.Patch</c> itself calls) is
///     honored by <see cref="DnssecKeyManagementService.GenerateKeyAsync" /> on the very next call,
///     with no restart. Before this fix, <see cref="DnssecKeyManagementService" /> read
///     <see cref="DnssecAlgorithmPolicyOptions" /> via the usual, snapshotted
///     <see cref="IOptions{TOptions}" />, computed once and cached for the life of the process --
///     a write reaching the configuration file would never have been observed at all, even though
///     this service is itself registered scoped (a fresh instance per call/request), the same
///     shape this project's other options-bound classes each already found and fixed for their own
///     options class.
/// </summary>
public sealed class DnssecAlgorithmPolicyOptionsHotReloadTests
{
    private sealed class InMemoryDnssecKeyRepository : IDnssecKeyRepository
    {
        public readonly List<DnssecKeyEntity> Items = [];

        public Task<DnssecKeyEntity> GetByIdAsync(
            Guid id,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<DnssecKeyEntity>> ListAsync(
            System.Linq.Expressions.Expression<Func<DnssecKeyEntity, bool>> predicate = null,
            CancellationToken stoppingToken = default
        ) =>
            Task.FromResult<IReadOnlyList<DnssecKeyEntity>>(
                predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
            );

        public Task AddAsync(DnssecKeyEntity entity, CancellationToken stoppingToken = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(DnssecKeyEntity entity) { }

        public void Remove(DnssecKeyEntity entity) => Items.Remove(entity);

        public Task<IReadOnlyList<DnssecKeyEntity>> ListByZoneIdAsync(
            Guid zoneId,
            CancellationToken stoppingToken = default
        ) =>
            Task.FromResult<IReadOnlyList<DnssecKeyEntity>>([
                .. Items.Where(item => item.ZoneId == zoneId),
            ]);

        public Task<IReadOnlyList<DnssecKeyEntity>> SearchByZoneIdAsync(
            Guid zoneId,
            int skip,
            int take,
            CancellationToken stoppingToken = default
        ) =>
            Task.FromResult<IReadOnlyList<DnssecKeyEntity>>([
                .. Items
                    .Where(item => item.ZoneId == zoneId)
                    .OrderBy(item => item.Role)
                    .ThenBy(item => item.CreatedAtUtc)
                    .Skip(skip)
                    .Take(take),
            ]);

        public Task<int> CountByZoneIdAsync(
            Guid zoneId,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(Items.Count(item => item.ZoneId == zoneId));
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default) =>
            Task.FromResult(0);
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_ChangesWhatTheVeryNextGenerateKeyCallAllows_WithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-dnssec-algorithm-policy-hot-reload-{Guid.NewGuid():N}.json"
        );

        try
        {
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<DnssecAlgorithmPolicyOptions>(
                configuration.GetSection(DnssecAlgorithmPolicyOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<DnssecAlgorithmPolicyOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<DnssecAlgorithmPolicyOptions>
            >();

            // Before any write, the file does not exist yet -- this class's own compiled-in
            // default: no restriction at all.
            Assert.Empty(monitor.CurrentValue.AllowedAlgorithms);

            DnssecKeyManagementService service = new(
                new InMemoryDnssecKeyRepository(),
                new NoOpUnitOfWork(),
                new EphemeralDataProtectionProvider(),
                monitor
            );

            // Before the write, an Ed25519 key is allowed -- no restriction configured yet.
            DnssecKeyEntity before = await service.GenerateKeyAsync(
                Guid.NewGuid(),
                DnssecKeyRoleEnum.Ksk,
                DnssecAlgorithmEnum.Ed25519
            );
            Assert.Equal(DnssecAlgorithmEnum.Ed25519, before.Algorithm);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            // The exact same write mechanism DnssecPolicySettingsController.Patch itself calls --
            // restricting new-key generation to RSASHA256/ECDSAP256SHA256/ECDSAP384SHA384, the
            // FIPS preset's own restriction.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        DnssecAlgorithmPolicyOptions.SectionName,
                        nameof(DnssecAlgorithmPolicyOptions.AllowedAlgorithms),
                        new[]
                        {
                            DnssecAlgorithmEnum.RsaSha256.ToString(),
                            DnssecAlgorithmEnum.EcdsaP256Sha256.ToString(),
                            DnssecAlgorithmEnum.EcdsaP384Sha384.ToString(),
                        }
                    ),
                ],
                path
            );

            // .NET's own JSON configuration provider debounces file-change notifications by a
            // few hundred milliseconds -- this waits for that real reload to actually happen
            // rather than asserting against a fixed delay.
            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);
            Assert.Equal(3, monitor.CurrentValue.AllowedAlgorithms.Count);

            // The identical service instance, constructed before the write, now genuinely rejects
            // Ed25519 -- proof the toggle's own gate was actually re-evaluated rather than the
            // service simply always answering the same way regardless of configuration.
            await Assert.ThrowsAsync<ValidationException>(() =>
                service.GenerateKeyAsync(
                    Guid.NewGuid(),
                    DnssecKeyRoleEnum.Ksk,
                    DnssecAlgorithmEnum.Ed25519
                )
            );

            // An allowed algorithm still succeeds on that same instance.
            DnssecKeyEntity after = await service.GenerateKeyAsync(
                Guid.NewGuid(),
                DnssecKeyRoleEnum.Ksk,
                DnssecAlgorithmEnum.RsaSha256
            );
            Assert.Equal(DnssecAlgorithmEnum.RsaSha256, after.Algorithm);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
            }
        }
    }
}
