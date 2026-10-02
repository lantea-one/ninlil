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
///     This class checks that a change to <see cref="TsigServiceOptions" /> reaches
///     <see cref="TsigService.SignAsync" /> on the next call, without a restart. It writes the
///     setting through <see cref="Server.Presets.PresetConfigurationFileApplier" /> (what
///     <c>Ninlil.Web.Controllers.TsigSettingsController.Patch</c> calls) into a JSON file loaded
///     with reload-on-change, and uses an in-memory key repository.
///     <see cref="TsigService" /> must read its options through
///     <see cref="IOptionsMonitor{TOptions}" />; an <see cref="IOptions{TOptions}" /> value is
///     cached for the life of the process, even for a scoped service.
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

        public Task<IReadOnlyList<TsigKeyEntity>> ListUnusedClusterInviteKeysAsync(
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();
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
            // A reload-on-change JSON file source, the same kind the web host uses for its
            // configuration files.
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

            // The file does not exist yet, so this is the compiled-in default.
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

            // The same write TsigSettingsController.Patch makes.
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

            // The JSON provider debounces file-change notifications, so wait for the reload
            // event rather than a fixed delay.
            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);
            Assert.Equal((ushort)600, monitor.CurrentValue.DefaultFudgeSeconds);

            // The same instance, built before the write, now signs with the new fudge, so the
            // value is re-read per call rather than captured at construction.
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
                // Best-effort cleanup; a leftover temp file is harmless.
            }
        }
    }
}
