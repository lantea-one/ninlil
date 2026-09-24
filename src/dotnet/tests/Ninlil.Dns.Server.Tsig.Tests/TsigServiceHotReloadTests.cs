using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Tsig.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class proves, against the real .NET configuration/options binding pipeline and a real
///     reload-on-change JSON configuration file -- not merely
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read in isolation -- that a write
///     reaching <see cref="TsigServiceOptions" /> via
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (the exact mechanism
///     <c>Ninlil.Web.Controllers.TsigSettingsController.Patch</c> itself calls) is honored
///     by <see cref="TsigService.SignAsync" /> on the very next call, with no restart. Before this
///     fix, <see cref="TsigService" /> read <see cref="TsigServiceOptions" /> via the usual,
///     snapshotted <see cref="IOptions{TOptions}" />, computed once and cached for the life of the
///     process -- a write reaching the configuration file would never have been observed at all,
///     even though this service is itself registered scoped (a fresh instance per call), the same
///     shape this project's other options-bound classes each already found and fixed for their own
///     options class.
/// </summary>
public sealed class TsigServiceHotReloadTests
{
    private sealed class SingleKeyTsigKeyRepository(TsigKeyEntity key) : ITsigKeyRepository
    {
        public Task<TsigKeyEntity> FindByNameAsync(
            string name,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(name == key.Name ? key : null);

        public Task<TsigKeyEntity> GetByIdAsync(
            Guid id,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TsigKeyEntity>> ListAsync(
            System.Linq.Expressions.Expression<Func<TsigKeyEntity, bool>> predicate = null,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        public Task AddAsync(TsigKeyEntity entity, CancellationToken stoppingToken = default) =>
            throw new NotSupportedException();

        public void Update(TsigKeyEntity entity) => throw new NotSupportedException();

        public void Remove(TsigKeyEntity entity) => throw new NotSupportedException();

        public Task<IReadOnlyList<TsigKeyEntity>> SearchAsync(
            int skip,
            int take,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken stoppingToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_ChangesTheFudgeTheVeryNextSignAsyncCallEmbeds_WithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-tsig-fudge-hot-reload-{Guid.NewGuid():N}.json"
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
            services.Configure<TsigServiceOptions>(
                configuration.GetSection(TsigServiceOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<TsigServiceOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<TsigServiceOptions>
            >();

            // Before any write, the file does not exist yet -- this class's own compiled-in
            // default, RFC 8945's own recommended value.
            Assert.Equal((ushort)300, monitor.CurrentValue.DefaultFudgeSeconds);

            TsigSecretProtector protector = new(new EphemeralDataProtectionProvider());
            TsigKeyEntity key = new()
            {
                Name = "test-key",
                AlgorithmName = "hmac-sha256.",
                SecretEncrypted = protector.Protect(new byte[32]),
                IsDisabled = false,
            };

            TsigService service = new(new SingleKeyTsigKeyRepository(key), protector, monitor);

            DnsMessage before = await service.SignAsync(
                new DnsMessage { Id = 1, IsResponse = true },
                key.Name,
                1
            );
            Assert.Equal((ushort)300, before.Tsig.Fudge);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            // The exact same write mechanism TsigSettingsController.Patch itself calls.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        TsigServiceOptions.SectionName,
                        nameof(TsigServiceOptions.DefaultFudgeSeconds),
                        (ushort)600
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
            Assert.Equal((ushort)600, monitor.CurrentValue.DefaultFudgeSeconds);

            // The identical service instance, constructed before the write, now genuinely signs
            // with the new fudge -- proof the value was actually re-read rather than the service
            // capturing it once at construction.
            DnsMessage after = await service.SignAsync(
                new DnsMessage { Id = 2, IsResponse = true },
                key.Name,
                2
            );
            Assert.Equal((ushort)600, after.Tsig.Fudge);
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
