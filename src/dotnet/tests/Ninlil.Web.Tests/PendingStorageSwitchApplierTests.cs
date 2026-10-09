using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Backup.Extension;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Ninlil.Web.Services.Storage;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="PendingStorageSwitchApplier" /> without a web host. Each test
///     builds a <see cref="ServiceCollection" /> shaped like the host's storage part
///     (<c>AddNinlilDnsEntityFrameworkCore</c>, the DHCP repositories, the backup service, an
///     ephemeral Data Protection provider) over real SQLite files in a temporary directory, and a
///     runtime JSON file that is a configuration source. It covers the copy when the runtime file
///     decides the storage settings, including that a fresh provider built from the rewritten
///     file reads the target (the restart), the copy when the operator already pointed the
///     configuration at the target, the cases that keep the record, and the failure rules.
///     It joins the shared collection because the backup service has one static restore gate
///     that the host's own restore tests also use.
/// </summary>
[Collection(WebTestCollection.Name)]
public sealed class PendingStorageSwitchApplierTests : IDisposable
{
    private readonly string _directory = Directory
        .CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"ninlil-switch-applier-{Guid.NewGuid():N}")
        )
        .FullName;

    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (ServiceProvider provider in _providers)
            provider.Dispose();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private string DatabasePath(string name) => Path.Combine(_directory, $"{name}.sqlite3");

    private static string ConnectionString(string path) => $"Data Source={path}";

    private string WriteRuntimeFile(string databasePath)
    {
        string path = Path.Combine(_directory, "runtime.json");
        File.WriteAllText(
            path,
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    Storage = new
                    {
                        Engine = "Sqlite",
                        ConnectionString = ConnectionString(databasePath),
                    },
                }
            )
        );
        return path;
    }

    /// <summary>
    ///     This class collects log entries so a test can check that a warning was written.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose() { }

        private sealed class CapturingLogger(
            ConcurrentQueue<(LogLevel Level, string Message)> entries
        ) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter
            ) => entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>
    ///     This class bundles a provider with the pieces a test inspects.
    /// </summary>
    private sealed record Rig(
        ServiceProvider Provider,
        PendingStorageSwitchApplier Applier,
        PendingStorageSwitchStore Store,
        StorageSwitchOutcomeRecorder Outcome,
        StorageOptions Options,
        CapturingLoggerProvider Log,
        string RuntimeFile
    );

    /// <summary>
    ///     This method builds a provider whose configuration is the runtime file and, when
    ///     <paramref name="externalStoragePath" /> is given, an in-memory source after it that
    ///     sets the storage keys (the same effect as environment variables).
    /// </summary>
    private Rig BuildRig(
        string runtimeFile,
        IDataProtectionProvider dataProtection = null,
        string externalStoragePath = null
    )
    {
        ConfigurationBuilder configurationBuilder = new();
        configurationBuilder.AddJsonFile(runtimeFile, optional: false, reloadOnChange: false);
        if (externalStoragePath is not null)
            configurationBuilder.AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["Storage:Engine"] = "Sqlite",
                    ["Storage:ConnectionString"] = ConnectionString(externalStoragePath),
                }
            );
        IConfigurationRoot configuration = configurationBuilder.Build();

        CapturingLoggerProvider log = new();
        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(log));
        services.AddSingleton(dataProtection ?? new EphemeralDataProtectionProvider());
        services.AddNinlilDnsEntityFrameworkCore(configuration);
        services.AddNinlilDhcpEntityFrameworkCore();
        services.AddSingleton(
            new TlsCertificateOptions
            {
                PfxPath = "",
                SelfSignedCertificateStatePath = Path.Combine(_directory, "no-certificate.pfx"),
            }
        );
        services.AddNinlilDnsServerBackup(configuration);
        services.PostConfigure<BackupOptions>(options =>
        {
            options.DataProtectionKeyRingDirectory = _directory;
            options.RuntimeConfigurationFilePath = runtimeFile;
        });

        RuntimeConfigurationOptions runtimeOptions = new() { ConfigurationFilePath = runtimeFile };
        services.AddSingleton(runtimeOptions);
        services.AddSingleton(provider => new PendingStorageSwitchStore(
            _directory,
            provider.GetRequiredService<IDataProtectionProvider>(),
            provider.GetRequiredService<ILogger<PendingStorageSwitchStore>>()
        ));
        services.AddSingleton<StorageSwitchOutcomeRecorder>();
        services.AddSingleton(provider => new PendingStorageSwitchApplier(
            provider.GetRequiredService<PendingStorageSwitchStore>(),
            provider.GetRequiredService<StorageOptions>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            runtimeOptions,
            provider.GetRequiredService<IOptions<BackupOptions>>(),
            provider.GetRequiredService<StorageSwitchOutcomeRecorder>(),
            provider.GetRequiredService<ILogger<PendingStorageSwitchApplier>>()
        ));

        ServiceProvider built = services.BuildServiceProvider();
        _providers.Add(built);
        return new Rig(
            built,
            built.GetRequiredService<PendingStorageSwitchApplier>(),
            built.GetRequiredService<PendingStorageSwitchStore>(),
            built.GetRequiredService<StorageSwitchOutcomeRecorder>(),
            built.GetRequiredService<StorageOptions>(),
            log,
            runtimeFile
        );
    }

    private static async Task MigrateAsync(string databasePath)
    {
        await using NinlilDnsContext context = OpenContext(databasePath);
        await context.Database.MigrateAsync();
    }

    private static NinlilDnsContext OpenContext(string databasePath) =>
        new(
            (DbContextOptions<NinlilDnsContext>)
                new DbContextOptionsBuilder<NinlilDnsContext>()
                    .UseNinlilDnsStorage(
                        new StorageOptions
                        {
                            Engine = StorageEngineEnum.Sqlite,
                            ConnectionString = ConnectionString(databasePath),
                        }
                    )
                    .Options
        );

    private static async Task AddZoneDirectlyAsync(string databasePath, string name)
    {
        await using NinlilDnsContext context = OpenContext(databasePath);
        context
            .Set<ZoneEntity>()
            .Add(
                new ZoneEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Type = ZoneTypeEnum.Primary,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await context.SaveChangesAsync();
    }

    private static async Task<string[]> ZoneNamesAsync(ServiceProvider provider)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        return
        [
            .. (await scope.ServiceProvider.GetRequiredService<IZoneRepository>().ListAsync())
                .Select(zone => zone.Name)
                .Order(),
        ];
    }

    private Task SaveRecordAsync(Rig rig, string sourcePath, string targetPath) =>
        rig.Store.SaveAsync(
            new PendingStorageSwitch(
                StorageEngineEnum.Sqlite,
                ConnectionString(sourcePath),
                StorageEngineEnum.Sqlite,
                ConnectionString(targetPath),
                DateTimeOffset.UtcNow,
                "tester"
            ),
            CancellationToken.None
        );

    [Fact]
    public async Task Apply_WhenTheRuntimeFileDecidesAndTheSourceIsConfigured_CopiesAndTheNextStartReadsTheTarget()
    {
        string a = DatabasePath("a");
        string b = DatabasePath("b");
        await MigrateAsync(a);
        await AddZoneDirectlyAsync(a, "z1.example");
        string runtime = WriteRuntimeFile(a);
        Rig rig = BuildRig(runtime);
        await SaveRecordAsync(rig, a, b);

        await rig.Applier.ApplyAsync();

        // The runtime file now names B, the record is gone and the outcome says what was copied.
        IConfigurationRoot rewritten = new ConfigurationBuilder().AddJsonFile(runtime).Build();
        Assert.Equal("Sqlite", rewritten["Storage:Engine"]);
        Assert.Equal(ConnectionString(b), rewritten["Storage:ConnectionString"]);
        Assert.Null(rig.Store.Peek());
        Assert.True(rig.Outcome.Current.Succeeded);
        Assert.True(rig.Outcome.Current.RowsCopied > 0);

        // A write to A after the switch is not in B. A fresh provider built from the rewritten
        // file, which is what the next start does, reads B.
        await AddZoneDirectlyAsync(a, "z2.example");
        Rig restarted = BuildRig(runtime);
        string[] zones = await ZoneNamesAsync(restarted.Provider);
        Assert.Contains("z1.example", zones);
        Assert.DoesNotContain("z2.example", zones);
        Assert.Equal(ConnectionString(b), restarted.Options.ConnectionString);
    }

    [Fact]
    public async Task Apply_WhenTheConfigurationAlreadyPointsAtTheTarget_CopiesAndLeavesTheFileAlone()
    {
        string a = DatabasePath("a");
        string b = DatabasePath("b");
        await MigrateAsync(a);
        await AddZoneDirectlyAsync(a, "z1.example");

        // The startup migrator has already migrated the configured target.
        await MigrateAsync(b);
        string runtime = WriteRuntimeFile(a);
        string runtimeBefore = File.ReadAllText(runtime);
        Rig rig = BuildRig(runtime, externalStoragePath: b);
        await SaveRecordAsync(rig, a, b);

        await rig.Applier.ApplyAsync();

        Assert.Equal(ConnectionString(b), rig.Options.ConnectionString);
        Assert.Contains("z1.example", await ZoneNamesAsync(rig.Provider));
        Assert.Equal(runtimeBefore, File.ReadAllText(runtime));
        Assert.Null(rig.Store.Peek());
        Assert.True(rig.Outcome.Current.Succeeded);
    }

    [Fact]
    public async Task Apply_WhenAnotherSourceNowDecidesTheStorage_KeepsTheRecordAndWarns()
    {
        string a = DatabasePath("a");
        string b = DatabasePath("b");
        await MigrateAsync(a);
        string runtime = WriteRuntimeFile(a);
        string runtimeBefore = File.ReadAllText(runtime);
        Rig rig = BuildRig(runtime, externalStoragePath: a);
        await SaveRecordAsync(rig, a, b);

        await rig.Applier.ApplyAsync();

        Assert.NotNull(rig.Store.Peek());
        Assert.Null(rig.Outcome.Current);
        Assert.False(File.Exists(b));
        Assert.Equal(runtimeBefore, File.ReadAllText(runtime));
        Assert.Contains(rig.Log.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Apply_WhenTheConfiguredStorageIsNeitherEnd_KeepsTheRecordAndWarns()
    {
        string a = DatabasePath("a");
        await MigrateAsync(a);
        Rig rig = BuildRig(WriteRuntimeFile(a));
        await SaveRecordAsync(rig, DatabasePath("x"), DatabasePath("y"));

        await rig.Applier.ApplyAsync();

        Assert.NotNull(rig.Store.Peek());
        Assert.Null(rig.Outcome.Current);
        Assert.Equal(ConnectionString(a), rig.Options.ConnectionString);
        Assert.Contains(
            rig.Log.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("neither")
        );
    }

    [Fact]
    public async Task Apply_WhenTheTargetCannotBeFilled_KeepsServingTheSourceAndDeletesTheRecord()
    {
        string a = DatabasePath("a");
        await MigrateAsync(a);
        await AddZoneDirectlyAsync(a, "z1.example");
        string runtime = WriteRuntimeFile(a);
        string runtimeBefore = File.ReadAllText(runtime);

        // A directory cannot be opened as a database file.
        string target = Directory.CreateDirectory(Path.Combine(_directory, "not-a-file")).FullName;
        Rig rig = BuildRig(runtime);
        await SaveRecordAsync(rig, a, target);

        await rig.Applier.ApplyAsync();

        Assert.Equal(ConnectionString(a), rig.Options.ConnectionString);
        Assert.Null(rig.Store.Peek());
        Assert.False(rig.Outcome.Current.Succeeded);
        Assert.Equal(0, rig.Outcome.Current.RowsCopied);
        Assert.Equal(runtimeBefore, File.ReadAllText(runtime));
        Assert.Contains("z1.example", await ZoneNamesAsync(rig.Provider));
    }

    [Fact]
    public async Task Apply_WhenTheConfiguredTargetCannotBeFilled_RefusesStartupAndKeepsTheRecord()
    {
        string b = DatabasePath("b");
        await MigrateAsync(b);
        string source = Directory.CreateDirectory(Path.Combine(_directory, "not-a-file")).FullName;
        string runtime = WriteRuntimeFile(b);
        Rig rig = BuildRig(runtime, externalStoragePath: b);
        await SaveRecordAsync(rig, source, b);

        await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() => rig.Applier.ApplyAsync());

        Assert.NotNull(rig.Store.Peek());
        Assert.Equal(ConnectionString(b), rig.Options.ConnectionString);
        Assert.False(rig.Outcome.Current.Succeeded);
    }

    [Fact]
    public async Task Apply_WhenTheRecordCannotBeDecrypted_DeletesItAndChangesNothing()
    {
        string a = DatabasePath("a");
        await MigrateAsync(a);
        string runtime = WriteRuntimeFile(a);

        // One key ring writes the record; a different one (a replaced key ring) reads it.
        Rig writer = BuildRig(runtime, new EphemeralDataProtectionProvider());
        await SaveRecordAsync(writer, a, DatabasePath("b"));
        Rig reader = BuildRig(runtime, new EphemeralDataProtectionProvider());

        await reader.Applier.ApplyAsync();

        Assert.Null(reader.Store.Peek());
        Assert.Null(reader.Outcome.Current);
        Assert.False(File.Exists(DatabasePath("b")));
        Assert.Contains(reader.Log.Entries, entry => entry.Level == LogLevel.Error);
    }
}
