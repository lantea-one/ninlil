using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.RateLimiting;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the settings cluster-sync wire end to end without a web host.
///     <see cref="ClusterConfigSyncSettingsRecorder" /> records a write against in-memory fakes and
///     captures the published event, then <see cref="SettingsClusterConfigSyncApplier" /> applies
///     that payload to a temporary configuration file standing in for the peer's. The tests assert
///     that lists, doubles, integers, booleans, strings, <see cref="TimeSpan" /> strings, enums and
///     <see langword="null" /> reach the peer file with their JSON types, that the file binds into
///     the real options classes, and that a payload in the old string-only shape still applies.
/// </summary>
public class SettingsClusterSyncRoundTripTests : IDisposable
{
    private readonly string peerFilePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-sync-roundtrip-{Guid.NewGuid():N}.json"
    );

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(peerFilePath))
            File.Delete(peerFilePath);
    }

    [Fact]
    public async Task RoundTrip_OfEveryJsonType_ArrivesInThePeerFileUnchanged()
    {
        string payload = await RecordAsync(
            DnsRateLimitOptions.SectionName,
            [
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    "SubnetBypassCidrs",
                    new List<string> { "10.0.0.0/8", "fd00::/8" }
                ),
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    "QueriesPerSecond",
                    2.5
                ),
                new PresetConfigurationSetting(DnsRateLimitOptions.SectionName, "BurstSize", 42),
                new PresetConfigurationSetting(DnsRateLimitOptions.SectionName, "Enable", true),
                new PresetConfigurationSetting(DnsRateLimitOptions.SectionName, "Note", "hello"),
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    "CleanupInterval",
                    TimeSpan.FromMinutes(2).ToString()
                ),
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    "Level",
                    DayOfWeek.Friday
                ),
                new PresetConfigurationSetting(DnsRateLimitOptions.SectionName, "Gone", null),
            ]
        );

        await ApplyAsync(payload);

        JsonObject section = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(peerFilePath))["DnsServer"]["RateLimiting"];
        Assert.Equal(
            ["10.0.0.0/8", "fd00::/8"],
            section["SubnetBypassCidrs"].AsArray().Select(node => node.GetValue<string>())
        );
        Assert.Equal(JsonValueKind.Number, section["QueriesPerSecond"].GetValueKind());
        Assert.Equal(2.5, section["QueriesPerSecond"].GetValue<double>());
        Assert.Equal(42, section["BurstSize"].GetValue<int>());
        Assert.Equal(JsonValueKind.True, section["Enable"].GetValueKind());
        Assert.Equal("hello", section["Note"].GetValue<string>());
        Assert.Equal("00:02:00", section["CleanupInterval"].GetValue<string>());
        // An enum is serialized as its number by default; the binder reads either form.
        Assert.Equal(JsonValueKind.Number, section["Level"].GetValueKind());
        Assert.True(section.ContainsKey("Gone"));
        Assert.Null(section["Gone"]);

        DnsRateLimitOptions bound = new ConfigurationBuilder()
            .AddJsonFile(peerFilePath)
            .Build()
            .GetSection(DnsRateLimitOptions.SectionName)
            .Get<DnsRateLimitOptions>();
        Assert.Equal(["10.0.0.0/8", "fd00::/8"], bound.SubnetBypassCidrs);
        Assert.Equal(2.5, bound.QueriesPerSecond);
        Assert.Equal(42, bound.BurstSize);
        Assert.True(bound.Enable);
        Assert.Equal(TimeSpan.FromMinutes(2), bound.CleanupInterval);
    }

    [Fact]
    public async Task RoundTrip_OfADoubleUnderAGermanCulture_StaysAnInvariantNumber()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            string payload = await RecordAsync(
                DnsRateLimitOptions.SectionName,
                [
                    new PresetConfigurationSetting(
                        DnsRateLimitOptions.SectionName,
                        "QueriesPerSecond",
                        2.5
                    ),
                ]
            );

            // The decimal comma of de-DE must not appear on the wire.
            Assert.DoesNotContain("2,5", payload);

            await ApplyAsync(payload);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        DnsRateLimitOptions bound = new ConfigurationBuilder()
            .AddJsonFile(peerFilePath)
            .Build()
            .GetSection(DnsRateLimitOptions.SectionName)
            .Get<DnsRateLimitOptions>();
        Assert.Equal(2.5, bound.QueriesPerSecond);
    }

    [Fact]
    public async Task RoundTrip_OfReverseProxyLists_BindsIntoReverseProxyOptions()
    {
        string payload = await RecordAsync(
            ReverseProxyOptions.SectionName,
            [
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    "KnownProxies",
                    new List<string> { "192.0.2.1", "2001:db8::1" }
                ),
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    "KnownNetworks",
                    new List<string> { "198.51.100.0/24" }
                ),
                new PresetConfigurationSetting(ReverseProxyOptions.SectionName, "Enable", true),
                new PresetConfigurationSetting(
                    ReverseProxyOptions.SectionName,
                    "SharedSecret",
                    null
                ),
            ]
        );

        await ApplyAsync(payload);

        ReverseProxyOptions bound = new ConfigurationBuilder()
            .AddJsonFile(peerFilePath)
            .Build()
            .GetSection(ReverseProxyOptions.SectionName)
            .Get<ReverseProxyOptions>();
        Assert.Equal(["192.0.2.1", "2001:db8::1"], bound.KnownProxies);
        Assert.Equal(["198.51.100.0/24"], bound.KnownNetworks);
        Assert.True(bound.Enable);
        Assert.Null(bound.SharedSecret);
    }

    [Fact]
    public async Task Apply_OfAnOldStringOnlyPayload_StillWritesTheValues()
    {
        string oldPayload = JsonSerializer.Serialize(
            new[]
            {
                new
                {
                    SectionPath = DnsRateLimitOptions.SectionName,
                    Key = "BurstSize",
                    Value = "42",
                },
                new
                {
                    SectionPath = DnsRateLimitOptions.SectionName,
                    Key = "Enable",
                    Value = "True",
                },
                new
                {
                    SectionPath = DnsRateLimitOptions.SectionName,
                    Key = "QueriesPerSecond",
                    Value = "2.5",
                },
                new
                {
                    SectionPath = DnsRateLimitOptions.SectionName,
                    Key = "CleanupInterval",
                    Value = "00:03:00",
                },
            }
        );

        await ApplyAsync(oldPayload);

        DnsRateLimitOptions bound = new ConfigurationBuilder()
            .AddJsonFile(peerFilePath)
            .Build()
            .GetSection(DnsRateLimitOptions.SectionName)
            .Get<DnsRateLimitOptions>();
        Assert.Equal(42, bound.BurstSize);
        Assert.True(bound.Enable);
        Assert.Equal(2.5, bound.QueriesPerSecond);
        Assert.Equal(TimeSpan.FromMinutes(3), bound.CleanupInterval);
    }

    /// <summary>
    ///     This method runs the recorder against in-memory fakes and returns the published
    ///     <c>SettingsJson</c> payload.
    /// </summary>
    private static async Task<string> RecordAsync(
        string sectionName,
        List<PresetConfigurationSetting> settings
    )
    {
        CapturingPublisher publisher = new();
        ClusterConfigSyncSettingsRecorder recorder = new(
            new FakeStateRepository(),
            new FakeUnitOfWork(),
            new FakeClusterOptionsMonitor(new ClusterOptions { NodeId = Guid.NewGuid() }),
            publisher
        );

        await recorder.RecordAndPublishAsync(sectionName, settings, CancellationToken.None);

        Assert.Equal(sectionName, publisher.Published.SectionName);
        return publisher.Published.SettingsJson;
    }

    /// <summary>This method applies a payload to the temporary peer file through the applier.</summary>
    private async Task ApplyAsync(string payloadJson)
    {
        SettingsClusterConfigSyncApplier applier = new(
            new FakeStateRepository(),
            new FakeUnitOfWork(),
            new RuntimeConfigurationOptions { ConfigurationFilePath = peerFilePath },
            NullLogger<SettingsClusterConfigSyncApplier>.Instance
        );

        await applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                SettingsClusterConfigSyncEntityType.EntityTypeName,
                "test-section",
                false,
                payloadJson,
                DateTimeOffset.UtcNow,
                Guid.NewGuid()
            ),
            CancellationToken.None
        );
    }

    /// <summary>This class captures the one event the recorder publishes.</summary>
    private sealed class CapturingPublisher : IDnsServerEventPublisher
    {
        public ClusterConfigSettingsChangedEvent Published { get; private set; }

        public void Publish(IDnsServerEvent domainEvent) =>
            Published = (ClusterConfigSettingsChangedEvent)domainEvent;
    }

    /// <summary>This class is an in-memory state repository holding rows by section name.</summary>
    private sealed class FakeStateRepository : IClusterSyncedSettingsStateRepository
    {
        private readonly List<ClusterSyncedSettingsStateEntity> rows = [];

        public Task<ClusterSyncedSettingsStateEntity> FindBySectionNameAsync(
            string sectionName,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(rows.FirstOrDefault(row => row.SectionName == sectionName));

        public Task<ClusterSyncedSettingsStateEntity> GetByIdAsync(
            Guid id,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(rows.FirstOrDefault(row => row.Id == id));

        public Task<IReadOnlyList<ClusterSyncedSettingsStateEntity>> ListAsync(
            Expression<Func<ClusterSyncedSettingsStateEntity, bool>> predicate = null,
            CancellationToken stoppingToken = default
        ) => Task.FromResult<IReadOnlyList<ClusterSyncedSettingsStateEntity>>([.. rows]);

        public Task AddAsync(
            ClusterSyncedSettingsStateEntity entity,
            CancellationToken stoppingToken = default
        )
        {
            rows.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(ClusterSyncedSettingsStateEntity entity) { }

        public void Remove(ClusterSyncedSettingsStateEntity entity) => rows.Remove(entity);
    }

    /// <summary>This class is a unit of work whose save does nothing.</summary>
    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default) =>
            Task.FromResult(0);
    }

    /// <summary>This class is an options monitor that always returns one value.</summary>
    private sealed class FakeClusterOptionsMonitor(ClusterOptions value)
        : IOptionsMonitor<ClusterOptions>
    {
        public ClusterOptions CurrentValue => value;

        public ClusterOptions Get(string name) => value;

        public IDisposable OnChange(Action<ClusterOptions, string> listener) => null;
    }
}
