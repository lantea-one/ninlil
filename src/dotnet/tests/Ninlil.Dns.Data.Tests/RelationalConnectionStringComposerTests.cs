using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;
using Ninlil.Dns.Data.EntityFrameworkCore.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     These tests cover <see cref="StorageConnectionStringComposer" /> and the PostgreSQL, MySQL
///     and SQL Server composers behind it. They compose strings in memory and read the result
///     back; no driver connects. Every connection string carries a password, so the tests also
///     prove that no refusal message leaks it.
/// </summary>
public sealed class RelationalConnectionStringComposerTests
{
    private const string PostgreSqlSingle =
        "Host=primary;Database=ninlil;Username=u;Password=hunter2";
    private const string PostgreSqlMulti = "Host=a,b;Database=ninlil;Username=u;Password=hunter2";
    private const string MySqlSingle = "Server=primary;Database=ninlil;User=u;Password=hunter2";
    private const string SqlServerSingle =
        "Server=primary;Database=ninlil;User Id=u;Password=hunter2;TrustServerCertificate=True";

    private static StorageOptions Options(
        StorageEngineEnum engine,
        string primary,
        string replica = ""
    )
    {
        StorageOptions options = new() { Engine = engine, ConnectionString = primary };
        options.ReadReplica.ConnectionString = replica;
        return options;
    }

    private static DatabaseStartupRefusedException Refused(StorageOptions options)
    {
        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                StorageConnectionStringComposer.Compose(options)
        );
        Assert.DoesNotContain("hunter2", refusal.Message);
        return refusal;
    }

    [Theory]
    [InlineData(StorageEngineEnum.Sqlite, "Data Source=/tmp/x.sqlite3")]
    [InlineData(StorageEngineEnum.PostgreSql, PostgreSqlSingle)]
    [InlineData(StorageEngineEnum.MySql, MySqlSingle)]
    [InlineData(StorageEngineEnum.SqlServer, SqlServerSingle)]
    public void Compose_WithNoClusterSetting_ReturnsTheOperatorsStringUnchanged(
        StorageEngineEnum engine,
        string connectionString
    )
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(engine, connectionString)
        );

        Assert.Equal(connectionString, strings.Primary);
        Assert.Null(strings.Replica);
        Assert.Empty(strings.Warnings);
        Assert.Equal(connectionString, strings.SourceConnectionString);
    }

    [Fact]
    public void Compose_ForMongoDb_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            StorageConnectionStringComposer.Compose(Options(StorageEngineEnum.MongoDb, "x"))
        );
    }

    [Fact]
    public void IsComposedFrom_AfterTheStorageSwitchChangesTheOptions_IsFalse()
    {
        StorageOptions options = Options(StorageEngineEnum.PostgreSql, PostgreSqlSingle);
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(options);

        Assert.True(strings.IsComposedFrom(options));
        options.ConnectionString = PostgreSqlMulti;
        Assert.False(strings.IsComposedFrom(options));
        options.ConnectionString = PostgreSqlSingle;
        options.Engine = StorageEngineEnum.MySql;
        Assert.False(strings.IsComposedFrom(options));
    }

    // PostgreSQL

    [Fact]
    public void PostgreSql_PrimaryWithSeveralHosts_GetsReadWriteByDefault()
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(StorageEngineEnum.PostgreSql, PostgreSqlMulti)
        );

        Assert.Contains("Target Session Attributes=read-write", strings.Primary);
        Assert.Contains("Host=a,b", strings.Primary);
    }

    [Fact]
    public void PostgreSql_PrimaryWithOneHost_StaysUnchangedEvenWithoutOptions()
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(StorageEngineEnum.PostgreSql, PostgreSqlSingle)
        );

        Assert.Equal(PostgreSqlSingle, strings.Primary);
    }

    [Fact]
    public void PostgreSql_EveryPrimaryOption_LandsInTheString()
    {
        StorageOptions options = Options(StorageEngineEnum.PostgreSql, PostgreSqlMulti);
        options.PostgreSql.TargetSessionAttributes = "Primary";
        options.PostgreSql.LoadBalanceHosts = true;
        options.PostgreSql.HostRecheckSeconds = 3;

        string primary = StorageConnectionStringComposer.Compose(options).Primary;

        Assert.Contains("Target Session Attributes=primary", primary);
        Assert.Contains("Load Balance Hosts=True", primary);
        Assert.Contains("Host Recheck Seconds=3", primary);
    }

    [Theory]
    [InlineData("Standby")]
    [InlineData("PreferStandby")]
    [InlineData("Any")]
    [InlineData("nonsense")]
    public void PostgreSql_PrimaryAttributesOtherThanReadWriteOrPrimary_AreRefused(string value)
    {
        StorageOptions options = Options(StorageEngineEnum.PostgreSql, PostgreSqlMulti);
        options.PostgreSql.TargetSessionAttributes = value;

        Assert.Contains("TargetSessionAttributes", Refused(options).Message);
    }

    [Fact]
    public void PostgreSql_AnOptionThatContradictsTheString_IsRefusedNamingBothValues()
    {
        StorageOptions options = Options(
            StorageEngineEnum.PostgreSql,
            PostgreSqlMulti + ";Target Session Attributes=primary"
        );
        options.PostgreSql.TargetSessionAttributes = "ReadWrite";

        DatabaseStartupRefusedException refusal = Refused(options);

        Assert.Contains("read-write", refusal.Message);
        Assert.Contains("primary", refusal.Message);
    }

    [Theory]
    [InlineData("TargetSessionAttributes=primary")]
    [InlineData("Target_Session_Attributes=primary")]
    [InlineData("target session attributes=primary")]
    public void PostgreSql_EverySpellingOfTheKey_CountsAsPresent(string spelling)
    {
        StorageOptions options = Options(
            StorageEngineEnum.PostgreSql,
            PostgreSqlMulti + ";" + spelling
        );
        options.PostgreSql.TargetSessionAttributes = "ReadWrite";

        Refused(options);
    }

    [Fact]
    public void PostgreSql_AnOptionThatMatchesTheString_IsAccepted()
    {
        string source = PostgreSqlMulti + ";Target Session Attributes=read-write";
        StorageOptions options = Options(StorageEngineEnum.PostgreSql, source);
        options.PostgreSql.TargetSessionAttributes = "ReadWrite";

        Assert.Equal(source, StorageConnectionStringComposer.Compose(options).Primary);
    }

    [Fact]
    public void PostgreSql_LoadBalanceContradictingTheString_IsRefused()
    {
        StorageOptions options = Options(
            StorageEngineEnum.PostgreSql,
            PostgreSqlSingle + ";Load Balance Hosts=false"
        );
        options.PostgreSql.LoadBalanceHosts = true;

        Assert.Contains("LoadBalanceHosts", Refused(options).Message);
    }

    [Fact]
    public void PostgreSql_ReplicaDefaults_PreferStandbyAndLoadBalancing()
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(
                StorageEngineEnum.PostgreSql,
                PostgreSqlMulti,
                "Host=r1,r2;Database=ninlil;Username=u;Password=hunter2"
            )
        );

        Assert.Contains("Target Session Attributes=prefer-standby", strings.Replica);
        Assert.Contains("Load Balance Hosts=True", strings.Replica);
        Assert.Contains("Host=r1,r2", strings.Replica);
        Assert.Contains("Target Session Attributes=read-write", strings.Primary);
        Assert.DoesNotContain("Load Balance Hosts", strings.Primary);
    }

    [Fact]
    public void PostgreSql_ReplicaWithASingleHost_GetsNoTargetSessionAttributesOrBalancing()
    {
        // Npgsql refuses a Target Session Attributes value other than Any on a single host.
        string replica = StorageConnectionStringComposer
            .Compose(
                Options(
                    StorageEngineEnum.PostgreSql,
                    PostgreSqlSingle,
                    "Host=standby;Database=ninlil;Username=u;Password=hunter2"
                )
            )
            .Replica;

        Assert.DoesNotContain("Target Session Attributes", replica);
        Assert.DoesNotContain("Load Balance Hosts", replica);
        Assert.Equal("standby", new Npgsql.NpgsqlConnectionStringBuilder(replica).Host);
        using Npgsql.NpgsqlConnection connection = new(replica);
    }

    [Fact]
    public void PostgreSql_ReplicaOptions_LandInTheReplicaString()
    {
        StorageOptions options = Options(
            StorageEngineEnum.PostgreSql,
            PostgreSqlSingle,
            "Host=r1,r2;Database=ninlil;Username=u;Password=hunter2"
        );
        options.PostgreSql.ReplicaTargetSessionAttributes = "Standby";
        options.PostgreSql.LoadBalanceHosts = false;
        options.PostgreSql.HostRecheckSeconds = 20;

        string replica = StorageConnectionStringComposer.Compose(options).Replica;

        Assert.Contains("Target Session Attributes=standby", replica);
        Assert.Contains("Load Balance Hosts=False", replica);
        Assert.Contains("Host Recheck Seconds=20", replica);
    }

    [Theory]
    [InlineData("ReadWrite")]
    [InlineData("Primary")]
    public void PostgreSql_ReplicaAttributesThatReachThePrimary_AreRefused(string value)
    {
        StorageOptions options = Options(
            StorageEngineEnum.PostgreSql,
            PostgreSqlSingle,
            "Host=r;Database=ninlil;Password=hunter2"
        );
        options.PostgreSql.ReplicaTargetSessionAttributes = value;

        Assert.Contains("ReplicaTargetSessionAttributes", Refused(options).Message);
    }

    [Fact]
    public void PostgreSql_ReplicaStringWithReadWrite_IsRefused()
    {
        StorageOptions options = Options(
            StorageEngineEnum.PostgreSql,
            PostgreSqlSingle,
            "Host=r;Database=ninlil;Password=hunter2;Target Session Attributes=read-write"
        );

        Refused(options);
    }

    [Fact]
    public void PostgreSql_ReplicaOnTheSameHostAndDatabase_WarnsOnly()
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(StorageEngineEnum.PostgreSql, PostgreSqlSingle, PostgreSqlSingle)
        );

        Assert.Single(strings.Warnings);
        Assert.DoesNotContain("hunter2", strings.Warnings[0]);
        Assert.NotNull(strings.Replica);
    }

    [Theory]
    [InlineData(
        "Host=localhost;Port=5432;Database=ninlil",
        "Host=localhost;Port=5433;Database=ninlil",
        0
    )]
    [InlineData(
        "Host=localhost;Port=5432;Database=ninlil",
        "Host=localhost:5433;Database=ninlil",
        0
    )]
    [InlineData("Host=localhost:5432;Database=ninlil", "Host=localhost;Database=ninlil", 1)]
    [InlineData("Host=localhost;Database=ninlil", "Host=LOCALHOST;Port=5432;Database=ninlil", 1)]
    public void PostgreSql_ReplicaOnTheSameHostNameWithAnotherPort_WarnsOnlyWhenThePortMatches(
        string primary,
        string replica,
        int expectedWarnings
    )
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(StorageEngineEnum.PostgreSql, primary, replica)
        );

        Assert.Equal(expectedWarnings, strings.Warnings.Count);
    }

    [Fact]
    public void MySql_ReplicaOnTheSameHostNameWithAnotherPort_DoesNotWarn()
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(
                StorageEngineEnum.MySql,
                "Server=localhost;Port=3306;Database=ninlil;User=u;Password=hunter2",
                "Server=localhost;Port=3307;Database=ninlil;User=u;Password=hunter2"
            )
        );

        Assert.Empty(strings.Warnings);
    }

    [Fact]
    public void PostgreSql_UnparseableReplicaString_IsRefusedWithoutTheString()
    {
        StorageOptions options = Options(
            StorageEngineEnum.PostgreSql,
            PostgreSqlSingle,
            "this is not; a connection string=hunter2=="
        );

        Refused(options);
    }

    // MySQL

    [Fact]
    public void MySql_FailOverPrimary_LandsInTheString()
    {
        StorageOptions options = Options(
            StorageEngineEnum.MySql,
            "Server=a,b;Database=ninlil;User=u;Password=hunter2"
        );
        options.MySql.LoadBalance = "FailOver";

        Assert.Contains(
            "Load Balance=FailOver",
            StorageConnectionStringComposer.Compose(options).Primary
        );
    }

    [Theory]
    [InlineData("RoundRobin")]
    [InlineData("Random")]
    [InlineData("LeastConnections")]
    public void MySql_PrimaryModeOtherThanFailOver_IsRefused(string mode)
    {
        StorageOptions options = Options(StorageEngineEnum.MySql, MySqlSingle);
        options.MySql.LoadBalance = mode;

        Assert.Contains("FailOver", Refused(options).Message);
    }

    [Fact]
    public void MySql_TheSynonymKeyInTheString_CountsAsPresent()
    {
        StorageOptions options = Options(
            StorageEngineEnum.MySql,
            "Server=a,b;Database=ninlil;User=u;Password=hunter2;LoadBalance=Random"
        );
        options.MySql.LoadBalance = "FailOver";

        Refused(options);
    }

    [Fact]
    public void MySql_MultiHostPrimaryStringWithRoundRobin_IsRefused()
    {
        StorageOptions options = Options(
            StorageEngineEnum.MySql,
            "Server=a,b;Database=ninlil;User=u;Password=hunter2;Load Balance=RoundRobin"
        );

        Refused(options);
    }

    [Fact]
    public void MySql_SingleHostPrimaryStringWithRoundRobin_IsLeftAlone()
    {
        string source = MySqlSingle + ";Load Balance=RoundRobin";

        Assert.Equal(
            source,
            StorageConnectionStringComposer
                .Compose(Options(StorageEngineEnum.MySql, source))
                .Primary
        );
    }

    [Fact]
    public void MySql_ReplicaDefaultsToRoundRobinOnSeveralHosts()
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(
                StorageEngineEnum.MySql,
                MySqlSingle,
                "Server=r1,r2;Database=ninlil;User=u;Password=hunter2"
            )
        );

        Assert.Contains("Load Balance=RoundRobin", strings.Replica);
        Assert.Equal(MySqlSingle, strings.Primary);
    }

    [Fact]
    public void MySql_ReplicaOptionLandsInTheString()
    {
        StorageOptions options = Options(
            StorageEngineEnum.MySql,
            MySqlSingle,
            "Server=r1,r2;Database=ninlil;User=u;Password=hunter2"
        );
        options.MySql.ReplicaLoadBalance = "LeastConnections";

        Assert.Contains(
            "Load Balance=LeastConnections",
            StorageConnectionStringComposer.Compose(options).Replica
        );
    }

    [Fact]
    public void MySql_ReplicaOptionContradictingTheString_IsRefused()
    {
        StorageOptions options = Options(
            StorageEngineEnum.MySql,
            MySqlSingle,
            "Server=r1,r2;Database=ninlil;User=u;Password=hunter2;Load Balance=Random"
        );
        options.MySql.ReplicaLoadBalance = "RoundRobin";

        DatabaseStartupRefusedException refusal = Refused(options);

        Assert.Contains("RoundRobin", refusal.Message);
        Assert.Contains("Random", refusal.Message);
    }

    [Fact]
    public void MySql_UnknownMode_IsRefused()
    {
        StorageOptions options = Options(StorageEngineEnum.MySql, MySqlSingle, "Server=r");
        options.MySql.ReplicaLoadBalance = "Fastest";

        Refused(options);
    }

    [Fact]
    public void MySql_ReplicaOnTheSameServerAndDatabase_WarnsOnly()
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(StorageEngineEnum.MySql, MySqlSingle, MySqlSingle)
        );

        Assert.Single(strings.Warnings);
    }

    // SQL Server

    [Fact]
    public void SqlServer_ReplicaAlwaysGetsReadOnlyIntent()
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(
                StorageEngineEnum.SqlServer,
                SqlServerSingle,
                "Server=listener;Database=ninlil;User Id=u;Password=hunter2;TrustServerCertificate=True"
            )
        );

        Assert.Contains("Application Intent=ReadOnly", strings.Replica);
        Assert.Equal(SqlServerSingle, strings.Primary);
    }

    [Fact]
    public void SqlServer_ReplicaStringWithReadWriteIntent_IsRefused()
    {
        StorageOptions options = Options(
            StorageEngineEnum.SqlServer,
            SqlServerSingle,
            "Server=listener;Database=ninlil;Password=hunter2;ApplicationIntent=ReadWrite"
        );

        Assert.Contains("ReadOnly", Refused(options).Message);
    }

    [Fact]
    public void SqlServer_MultiSubnetFailover_LandsInBothStrings()
    {
        StorageOptions options = Options(
            StorageEngineEnum.SqlServer,
            SqlServerSingle,
            "Server=listener;Database=ninlil;User Id=u;Password=hunter2"
        );
        options.SqlServer.MultiSubnetFailover = true;

        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(options);

        Assert.Contains("Multi Subnet Failover=True", strings.Primary);
        Assert.Contains("Multi Subnet Failover=True", strings.Replica);
    }

    [Fact]
    public void SqlServer_MultiSubnetFailoverContradictingTheString_IsRefused()
    {
        StorageOptions options = Options(
            StorageEngineEnum.SqlServer,
            SqlServerSingle + ";MultiSubnetFailover=false"
        );
        options.SqlServer.MultiSubnetFailover = true;

        Refused(options);
    }

    [Fact]
    public void SqlServer_ReplicaOnTheSameServerAndDatabase_WarnsOnly()
    {
        StorageConnectionStrings strings = StorageConnectionStringComposer.Compose(
            Options(StorageEngineEnum.SqlServer, SqlServerSingle, SqlServerSingle)
        );

        Assert.Single(strings.Warnings);
        Assert.DoesNotContain("hunter2", strings.Warnings[0]);
    }
}
