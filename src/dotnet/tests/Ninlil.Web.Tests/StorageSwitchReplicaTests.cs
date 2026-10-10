using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.Abstractions.Topology;
using Ninlil.Dns.Data.EntityFrameworkCore.Topology;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Ninlil.Web.Services.Storage;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests the refusal of a storage switch request while read replica or cluster
///     settings are configured. It builds <see cref="StorageEngineSwitchService" /> by hand, with
///     no web host, over SQLite files in a temporary directory.
/// </summary>
public sealed class StorageSwitchReplicaTests : IDisposable
{
    private readonly string _directory = Directory
        .CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"ninlil-switch-replica-{Guid.NewGuid():N}")
        )
        .FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private StorageEngineSwitchService CreateService(StorageOptions options)
    {
        string runtimeFile = Path.Combine(_directory, "runtime.json");
        File.WriteAllText(runtimeFile, "{}");
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(runtimeFile)
            .Build();

        return new StorageEngineSwitchService(
            options,
            StorageConnectionStringComposer.Compose(options),
            new StorageReadRouter(false),
            new PendingStorageSwitchStore(
                _directory,
                new EphemeralDataProtectionProvider(),
                NullLogger<PendingStorageSwitchStore>.Instance
            ),
            new StorageSwitchOutcomeRecorder(),
            configuration,
            new RuntimeConfigurationOptions { ConfigurationFilePath = runtimeFile },
            NullLogger<StorageEngineSwitchService>.Instance
        );
    }

    private StorageEngineSwitchRequest SqliteTarget() =>
        new(StorageEngineEnum.Sqlite, $"Data Source={Path.Combine(_directory, "target.sqlite3")}");

    private static StorageOptions PostgreSql() =>
        new()
        {
            Engine = StorageEngineEnum.PostgreSql,
            ConnectionString = "Host=primary;Database=ninlil;Username=u;Password=hunter2",
        };

    [Fact]
    public async Task ScheduleSwitch_WithAReadReplicaConfigured_IsRefusedWithoutTheConnectionString()
    {
        StorageOptions options = PostgreSql();
        options.ReadReplica.ConnectionString =
            "Host=replica;Database=ninlil;Username=u;Password=hunter2";

        ValidationException refusal = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(options).ScheduleSwitchAsync(SqliteTarget(), "tester")
        );

        Assert.Contains("Remove the read-replica settings, switch", refusal.Message);
        Assert.DoesNotContain("hunter2", refusal.Message);
    }

    [Fact]
    public async Task ScheduleSwitch_WithAClusterSettingOfTheEngineInUse_IsRefused()
    {
        StorageOptions options = PostgreSql();
        options.PostgreSql.HostRecheckSeconds = 5;

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(options).ScheduleSwitchAsync(SqliteTarget(), "tester")
        );
    }

    [Fact]
    public async Task ScheduleSwitch_WithNoClusterSetting_IsSaved()
    {
        StorageOptions options = new()
        {
            Engine = StorageEngineEnum.Sqlite,
            ConnectionString = $"Data Source={Path.Combine(_directory, "source.sqlite3")}",
        };
        // A section of another engine and the read-your-own-writes windows do not count.
        options.MySql.LoadBalance = "FailOver";
        options.ReadReplica.StickyAfterWriteSeconds = 20;
        StorageEngineSwitchService service = CreateService(options);

        StorageSwitchScheduled scheduled = await service.ScheduleSwitchAsync(
            SqliteTarget(),
            "tester"
        );

        Assert.Equal(StorageEngineEnum.Sqlite, scheduled.TargetEngine);
        Assert.True(service.CancelSwitch());
    }
}
