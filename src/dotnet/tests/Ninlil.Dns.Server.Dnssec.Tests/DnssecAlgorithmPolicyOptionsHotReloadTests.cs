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
///     This class checks that a change to <see cref="DnssecAlgorithmPolicyOptions" /> reaches
///     <see cref="DnssecKeyManagementService.GenerateKeyAsync" /> on the next call, without a
///     restart. It writes the setting through
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (what
///     <c>Ninlil.Web.Controllers.DnssecPolicySettingsController.Patch</c> calls) into a JSON file
///     loaded with reload-on-change, and uses an in-memory key repository.
///     <see cref="DnssecKeyManagementService" /> must read the policy through
///     <see cref="IOptionsMonitor{TOptions}" />; an <see cref="IOptions{TOptions}" /> value is
///     cached for the life of the process, even for a scoped service.
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
        // A directory for this test rather than the shared temp directory: the file watcher
        // behind reloadOnChange reports every change in the directory it watches, and on a busy
        // machine unrelated churn there can overflow its queue and drop the change we wait for.
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-dnssec-algorithm-policy-hot-reload-")
            .FullName;
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            // A reload-on-change JSON file source, the same kind the web host uses for its
            // configuration files.
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

            // The file does not exist yet, so this is the compiled-in default: no restriction.
            Assert.Empty(monitor.CurrentValue.AllowedAlgorithms);

            DnssecKeyManagementService service = new(
                new InMemoryDnssecKeyRepository(),
                new NoOpUnitOfWork(),
                new EphemeralDataProtectionProvider(),
                monitor
            );

            // Before the write, an Ed25519 key is allowed.
            DnssecKeyEntity before = await service.GenerateKeyAsync(
                Guid.NewGuid(),
                DnssecKeyRoleEnum.Ksk,
                DnssecAlgorithmEnum.Ed25519
            );
            Assert.Equal(DnssecAlgorithmEnum.Ed25519, before.Algorithm);

            // The same write DnssecPolicySettingsController.Patch makes, limiting new keys to the
            // FIPS preset's list: RSASHA256, ECDSAP256SHA256 and ECDSAP384SHA384.
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

            // The JSON provider debounces file-change notifications by a few hundred milliseconds.
            // Wait up to ten seconds for the written value itself, not for the first change
            // notification: a notification can arrive for an earlier, partial state of the file
            // and reload before the final content is visible.
            for (
                int attempt = 0;
                attempt < 200 && monitor.CurrentValue.AllowedAlgorithms.Count != 3;
                attempt++
            )
                await Task.Delay(50);

            Assert.Equal(3, monitor.CurrentValue.AllowedAlgorithms.Count);

            // The same instance, built before the write, now rejects Ed25519, so the policy is
            // re-read per call.
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
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover temporary directory is harmless.
            }
        }
    }
}
