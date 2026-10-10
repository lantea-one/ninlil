using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore.Topology;
using Ninlil.Dns.Data.MongoDb.Topology;
using Ninlil.Web.Services.Storage;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="StorageTopologyDescription" />, the startup topology line. It
///     composes connection strings in memory; the line must name hosts and limits and never a
///     password, user name or database file name.
/// </summary>
public sealed class StorageTopologyDescriptionTests
{
    private static StorageOptions PostgreSql() =>
        new()
        {
            Engine = StorageEngineEnum.PostgreSql,
            ConnectionString = "Host=primary;Database=ninlil;Username=u;Password=hunter2",
        };

    [Fact]
    public void Describe_WithAReadReplica_NamesTheHostsAndLimitsButNoSecret()
    {
        StorageOptions options = PostgreSql();
        options.ReadReplica.ConnectionString =
            "Host=replica1,replica2:5433;Database=ninlil;Username=u;Password=hunter2";

        string text = StorageTopologyDescription.Describe(
            options,
            StorageConnectionStringComposer.Compose(options)
        );

        Assert.Contains("PostgreSql", text);
        Assert.Contains("primary:5432", text);
        Assert.Contains("replica1:5432", text);
        Assert.Contains("replica2:5433", text);
        Assert.Contains("10 s", text);
        Assert.Contains("5 s behind", text);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("Username", text);
    }

    [Fact]
    public void Describe_WithoutAReplica_SaysSo()
    {
        StorageOptions options = PostgreSql();

        string text = StorageTopologyDescription.Describe(
            options,
            StorageConnectionStringComposer.Compose(options)
        );

        Assert.Contains("primary primary:5432, no read replica", text);
        Assert.DoesNotContain("hunter2", text);
    }

    [Fact]
    public void Describe_ForSqlite_NamesALocalFile()
    {
        StorageOptions options = new()
        {
            Engine = StorageEngineEnum.Sqlite,
            ConnectionString = "Data Source=/var/lib/ninlil/secret-name.sqlite3",
        };

        string text = StorageTopologyDescription.Describe(
            options,
            StorageConnectionStringComposer.Compose(options)
        );

        Assert.Contains("a local file", text);
        Assert.DoesNotContain("secret-name", text);
    }

    [Fact]
    public void Describe_ForMongoDb_NamesTheMembersAndTheReadPreferenceButNoSecret()
    {
        StorageOptions options = new()
        {
            Engine = StorageEngineEnum.MongoDb,
            ConnectionString = "mongodb://u:hunter2@m1:27017,m2:27018/ninlil?replicaSet=rs0",
        };
        options.MongoDb.ReadPreference = "SecondaryPreferred";

        string text = StorageTopologyDescription.Describe(
            options,
            MongoClientSettingsComposer.Compose(options)
        );

        Assert.Contains("m1:27017, m2:27018", text);
        Assert.Contains("SecondaryPreferred", text);
        Assert.DoesNotContain("hunter2", text);
    }
}
