using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;
using Ninlil.Dns.Data.MongoDb.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     These tests cover <see cref="MongoClientSettingsComposer" />. They compose MongoDB URLs in
///     memory and read the result back with <see cref="MongoUrl" />; no server is contacted.
/// </summary>
public sealed class MongoClientSettingsComposerTests
{
    private const string Standalone = "mongodb://u:hunter2@host1:27017/ninlil";
    private const string ReplicaSetUrl =
        "mongodb://u:hunter2@h1:27017,h2:27017,h3:27017/ninlil?replicaSet=rs0";

    private static StorageOptions Options(string url) =>
        new() { Engine = StorageEngineEnum.MongoDb, ConnectionString = url };

    private static DatabaseStartupRefusedException Refused(StorageOptions options)
    {
        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                MongoClientSettingsComposer.Compose(options)
        );
        Assert.DoesNotContain("hunter2", refusal.Message);
        return refusal;
    }

    [Fact]
    public void Compose_WithNoSettingAndNoReplicaSet_ReturnsTheUrlUnchanged()
    {
        StorageConnectionStrings strings = MongoClientSettingsComposer.Compose(Options(Standalone));

        Assert.Equal(Standalone, strings.Primary);
        Assert.Null(strings.Replica);
        Assert.Empty(strings.Warnings);
    }

    [Fact]
    public void Compose_ReplicaSetOptionOnAStandaloneUrl_IsAppliedWithMajorityAndJournal()
    {
        StorageOptions options = Options(Standalone);
        options.MongoDb.ReplicaSet = "rs0";

        MongoUrl url = MongoUrl.Create(MongoClientSettingsComposer.Compose(options).Primary);

        Assert.Equal("rs0", url.ReplicaSetName);
        Assert.Equal(WriteConcern.WMode.Majority, url.W);
        Assert.True(url.Journal);
    }

    [Fact]
    public void Compose_ReplicaSetNamedByTheUrl_GetsMajorityByDefault()
    {
        MongoUrl url = MongoUrl.Create(
            MongoClientSettingsComposer.Compose(Options(ReplicaSetUrl)).Primary
        );

        Assert.Equal(WriteConcern.WMode.Majority, url.W);
        Assert.True(url.Journal);
        Assert.Equal(3, url.Servers.Count());
    }

    [Theory]
    [InlineData("Majority", "majority")]
    [InlineData("Acknowledged", "1")]
    [InlineData("W2", "2")]
    [InlineData("w9", "9")]
    public void Compose_WriteConcernOption_LandsInTheUrl(string setting, string expected)
    {
        StorageOptions options = Options(Standalone);
        options.MongoDb.WriteConcern = setting;
        options.MongoDb.Journal = false;

        MongoUrl url = MongoUrl.Create(MongoClientSettingsComposer.Compose(options).Primary);

        Assert.Equal(expected, url.W.ToString().ToLowerInvariant());
        Assert.False(url.Journal);
    }

    [Theory]
    [InlineData("W1")]
    [InlineData("W10")]
    [InlineData("Everyone")]
    public void Compose_UnknownWriteConcern_IsRefused(string setting)
    {
        StorageOptions options = Options(Standalone);
        options.MongoDb.WriteConcern = setting;

        Assert.Contains("WriteConcern", Refused(options).Message);
    }

    [Fact]
    public void Compose_OptionsThatContradictTheUrl_AreRefusedNamingBothValues()
    {
        StorageOptions replicaSet = Options(ReplicaSetUrl);
        replicaSet.MongoDb.ReplicaSet = "other";
        DatabaseStartupRefusedException one = Refused(replicaSet);
        Assert.Contains("other", one.Message);
        Assert.Contains("rs0", one.Message);

        StorageOptions writeConcern = Options(Standalone + "?w=2");
        writeConcern.MongoDb.WriteConcern = "Majority";
        DatabaseStartupRefusedException two = Refused(writeConcern);
        Assert.Contains("majority", two.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", two.Message);

        StorageOptions journal = Options(Standalone + "?journal=false");
        journal.MongoDb.Journal = true;
        Refused(journal);

        StorageOptions preference = Options(Standalone + "?readPreference=primaryPreferred");
        preference.MongoDb.ReadPreference = "Nearest";
        Refused(preference);
    }

    [Fact]
    public void Compose_OptionsThatMatchTheUrl_AreAccepted()
    {
        StorageOptions options = Options(ReplicaSetUrl + "&w=majority&journal=true");
        options.MongoDb.ReplicaSet = "rs0";
        options.MongoDb.WriteConcern = "Majority";
        options.MongoDb.Journal = true;

        Assert.Equal(
            ReplicaSetUrl + "&w=majority&journal=true",
            MongoClientSettingsComposer.Compose(options).Primary
        );
    }

    [Fact]
    public void ComposeReplicaReadPreference_WithoutASetting_OrPrimary_IsNull()
    {
        StorageOptions options = Options(Standalone);
        Assert.Null(MongoClientSettingsComposer.ComposeReplicaReadPreference(options));

        options.MongoDb.ReadPreference = "primary";
        Assert.Null(MongoClientSettingsComposer.ComposeReplicaReadPreference(options));
    }

    [Fact]
    public void ComposeReplicaReadPreference_WithModeAndStaleness_CarriesBoth()
    {
        StorageOptions options = Options(ReplicaSetUrl);
        options.MongoDb.ReadPreference = "SecondaryPreferred";
        options.MongoDb.MaxStalenessSeconds = 120;

        ReadPreference preference = MongoClientSettingsComposer.ComposeReplicaReadPreference(
            options
        );

        Assert.Equal(ReadPreferenceMode.SecondaryPreferred, preference.ReadPreferenceMode);
        Assert.Equal(TimeSpan.FromSeconds(120), preference.MaxStaleness);
    }

    [Fact]
    public void Compose_UnknownReadPreference_OrStalenessBelowTheDriverMinimum_IsRefused()
    {
        StorageOptions mode = Options(ReplicaSetUrl);
        mode.MongoDb.ReadPreference = "Anywhere";
        Assert.Contains("ReadPreference", Refused(mode).Message);

        StorageOptions staleness = Options(ReplicaSetUrl);
        staleness.MongoDb.ReadPreference = "Secondary";
        staleness.MongoDb.MaxStalenessSeconds = 30;
        Assert.Contains("MaxStalenessSeconds", Refused(staleness).Message);
    }

    [Fact]
    public void Compose_StalenessWithPrimaryReadPreference_WarnsOnly()
    {
        StorageOptions options = Options(ReplicaSetUrl);
        options.MongoDb.MaxStalenessSeconds = 120;

        Assert.Single(MongoClientSettingsComposer.Compose(options).Warnings);
    }

    [Fact]
    public void Compose_UnparseableUrl_IsRefusedWithoutTheUrl()
    {
        Refused(Options("not-a-mongo-url-hunter2"));
    }
}
