using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     These tests cover <see cref="StorageTopologyValidator" />, the driver-independent checks on
///     the storage cluster settings. They call it directly with in-memory options; no database is
///     involved.
/// </summary>
public sealed class StorageTopologyValidatorTests
{
    private const string Secret = "Password=hunter2-secret";

    [Fact]
    public void Validate_WithDefaultOptions_ReturnsNoWarnings()
    {
        Assert.Empty(StorageTopologyValidator.Validate(new StorageOptions()));
    }

    [Theory]
    [InlineData(StorageEngineEnum.Sqlite)]
    [InlineData(StorageEngineEnum.MongoDb)]
    public void Validate_ReplicaConnectionStringOnAnEngineWithoutOne_IsRefusedWithoutTheSecret(
        StorageEngineEnum engine
    )
    {
        StorageOptions options = new() { Engine = engine };
        options.ReadReplica.ConnectionString = $"Host=replica;{Secret}";

        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                StorageTopologyValidator.Validate(options)
        );

        Assert.Contains("Storage:ReadReplica:ConnectionString", refusal.Message);
        Assert.DoesNotContain("hunter2", refusal.Message);
        if (engine == StorageEngineEnum.MongoDb)
            Assert.Contains("Storage:MongoDb:ReadPreference", refusal.Message);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 11)]
    public void Validate_MaxLagNotBelowTheStickyWindow_IsRefused(int sticky, int maxLag)
    {
        StorageOptions options = new() { Engine = StorageEngineEnum.PostgreSql };
        options.ReadReplica.ConnectionString = "Host=replica";
        options.ReadReplica.StickyAfterWriteSeconds = sticky;
        options.ReadReplica.MaxLagSeconds = maxLag;

        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                StorageTopologyValidator.Validate(options)
        );

        Assert.Contains("MaxLagSeconds", refusal.Message);
        Assert.Contains("StickyAfterWriteSeconds", refusal.Message);
    }

    [Fact]
    public void Validate_MaxLagNotBelowTheStickyWindow_IsIgnoredWithoutAReplica()
    {
        StorageOptions options = new() { Engine = StorageEngineEnum.PostgreSql };
        options.ReadReplica.MaxLagSeconds = 30;

        Assert.Empty(StorageTopologyValidator.Validate(options));
    }

    [Fact]
    public void Validate_MongoDbSecondaryReadPreferenceWithLagNotBelowTheWindow_IsRefused()
    {
        StorageOptions options = new() { Engine = StorageEngineEnum.MongoDb };
        options.MongoDb.ReadPreference = "SecondaryPreferred";
        options.ReadReplica.MaxLagSeconds = 10;

        Assert.Throws<DatabaseStartupRefusedException>(() =>
            StorageTopologyValidator.Validate(options)
        );
    }

    [Theory]
    [InlineData(0, 5, 5)]
    [InlineData(10, -1, 5)]
    [InlineData(10, 5, 0)]
    public void Validate_OutOfRangeWindows_AreRefused(int sticky, int maxLag, int interval)
    {
        StorageOptions options = new();
        options.ReadReplica.StickyAfterWriteSeconds = sticky;
        options.ReadReplica.MaxLagSeconds = maxLag;
        options.ReadReplica.LagCheckIntervalSeconds = interval;

        Assert.Throws<DatabaseStartupRefusedException>(() =>
            StorageTopologyValidator.Validate(options)
        );
    }

    [Fact]
    public void Validate_SettingsOfAnotherEngine_AreWarningsNotRefusals()
    {
        StorageOptions options = new() { Engine = StorageEngineEnum.Sqlite };
        options.PostgreSql.HostRecheckSeconds = 3;
        options.MySql.LoadBalance = "FailOver";
        options.SqlServer.MultiSubnetFailover = true;
        options.MongoDb.ReplicaSet = "rs0";

        List<string> warnings = StorageTopologyValidator.Validate(options);

        Assert.Equal(4, warnings.Count);
        Assert.Contains(warnings, warning => warning.Contains("Storage:PostgreSql"));
        Assert.Contains(warnings, warning => warning.Contains("Storage:MySql"));
        Assert.Contains(warnings, warning => warning.Contains("Storage:SqlServer"));
        Assert.Contains(warnings, warning => warning.Contains("Storage:MongoDb"));
    }
}
