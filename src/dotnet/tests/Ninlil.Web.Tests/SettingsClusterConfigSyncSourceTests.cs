using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.RateLimiting;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Configuration;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="SettingsClusterConfigSyncSource" />, the backfill reader for
///     settings sections, without a web host. Each test writes a temporary runtime configuration
///     file the way a settings controller does (through
///     <see cref="PresetConfigurationFileApplier" /> and
///     <see cref="ConfigurationSecretSettings.ProtectForFile" />), seeds in-memory state rows, and
///     reads the envelopes the source yields. The last test applies a backfill envelope and the
///     live envelope of the same write to two peer files through the real
///     <see cref="SettingsClusterConfigSyncApplier" /> and compares the files.
/// </summary>
public sealed class SettingsClusterConfigSyncSourceTests : IDisposable
{
    private readonly string filePath = TempFile("source");

    private readonly string liveFilePath = TempFile("live");

    private readonly string backfillFilePath = TempFile("backfill");

    private readonly Guid ownNodeId = Guid.NewGuid();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (string path in new[] { filePath, liveFilePath, backfillFilePath })
            if (File.Exists(path))
                File.Delete(path);
    }

    [Fact]
    public async Task ReadBackfillAsync_ForASectionWithAStateRow_SendsEveryCataloguedKeyTheFileHolds()
    {
        IDataProtectionProvider keyRing = new EphemeralDataProtectionProvider();
        await WriteFileAsync(keyRing, SampleSettings(DnsRateLimitOptions.SectionName));
        // A key nobody catalogued is not sent, whatever the file holds.
        await PresetConfigurationFileApplier.ApplyAsync(
            [new PresetConfigurationSetting(DnsRateLimitOptions.SectionName, "NotSynced", 1)],
            filePath
        );
        Guid originNodeId = Guid.NewGuid();
        DateTimeOffset writtenAt = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(state, DnsRateLimitOptions.SectionName, writtenAt, originNodeId);

        List<ClusterConfigSyncEnvelope> envelopes = await ReadAsync(keyRing, state);

        ClusterConfigSyncEnvelope envelope = Assert.Single(envelopes);
        Assert.Equal(SettingsClusterConfigSyncEntityType.EntityTypeName, envelope.EntityType);
        Assert.Equal(DnsRateLimitOptions.SectionName, envelope.EntityKey);
        Assert.False(envelope.IsDeleted);
        Assert.Equal(writtenAt, envelope.LastModifiedUtc);
        Assert.Equal(originNodeId, envelope.OriginatingNodeId);
        Assert.Equal(
            CataloguedKeys(DnsRateLimitOptions.SectionName),
            Entries(envelope).Select(entry => (entry.SectionPath, entry.Key)).Order()
        );
        Assert.Equal(
            2.5,
            Entries(envelope).Single(entry => entry.Key == "QueriesPerSecond").Value.GetDouble()
        );
        Assert.Equal(
            ["10.0.0.0/8", "fd00::/8"],
            Entries(envelope)
                .Single(entry => entry.Key == "SubnetBypassCidrs")
                .Value.EnumerateArray()
                .Select(element => element.GetString())
        );
    }

    [Fact]
    public async Task ReadBackfillAsync_ForARowWithoutAnOriginatingNode_StampsThisNodesId()
    {
        IDataProtectionProvider keyRing = new EphemeralDataProtectionProvider();
        await WriteFileAsync(keyRing, SampleSettings(DnsCookieOptions.SectionName));
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(state, DnsCookieOptions.SectionName, DateTimeOffset.UtcNow, null);

        ClusterConfigSyncEnvelope envelope = Assert.Single(await ReadAsync(keyRing, state));

        Assert.Equal(ownNodeId, envelope.OriginatingNodeId);
    }

    [Fact]
    public async Task ReadBackfillAsync_ForASectionWithoutAStateRow_SendsNothing()
    {
        IDataProtectionProvider keyRing = new EphemeralDataProtectionProvider();
        await WriteFileAsync(
            keyRing,
            SampleSettings(DnsCookieOptions.SectionName, DnsRewriteOptions.SectionName)
        );
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(state, DnsRewriteOptions.SectionName, DateTimeOffset.UtcNow, null);

        List<ClusterConfigSyncEnvelope> envelopes = await ReadAsync(keyRing, state);

        // Only the section that has a row is sent; cookies has none.
        Assert.Equal(DnsRewriteOptions.SectionName, Assert.Single(envelopes).EntityKey);
    }

    [Fact]
    public async Task ReadBackfillAsync_ForAMissingFile_SendsNothing()
    {
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(state, DnsCookieOptions.SectionName, DateTimeOffset.UtcNow, null);

        Assert.Empty(await ReadAsync(new EphemeralDataProtectionProvider(), state));
    }

    [Fact]
    public async Task ReadBackfillAsync_ForAnEncryptedSecret_SendsThePlaintextUnderThePlainKey()
    {
        IDataProtectionProvider keyRing = new EphemeralDataProtectionProvider();
        await WriteFileAsync(keyRing, SampleSettings(ReverseProxyOptions.SectionName));
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(state, ReverseProxyOptions.SectionName, DateTimeOffset.UtcNow, null);

        // The premise: the file holds the secret only as ciphertext.
        JsonObject fileSection = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(filePath))["DnsServer"]["ReverseProxy"];
        Assert.Equal(string.Empty, fileSection["SharedSecret"].GetValue<string>());
        string ciphertext = fileSection["SharedSecretEncrypted"].GetValue<string>();
        Assert.NotEmpty(ciphertext);

        ClusterConfigSyncEnvelope envelope = Assert.Single(await ReadAsync(keyRing, state));

        Assert.Equal(
            "s3cret",
            Entries(envelope).Single(entry => entry.Key == "SharedSecret").Value.GetString()
        );
        Assert.DoesNotContain("SharedSecretEncrypted", envelope.PayloadJson);
        Assert.DoesNotContain(ciphertext, envelope.PayloadJson);
    }

    [Fact]
    public async Task ReadBackfillAsync_ForAnEventBrokerPassword_SendsThePlaintextUnderTheBrokerPath()
    {
        IDataProtectionProvider keyRing = new EphemeralDataProtectionProvider();
        await WriteFileAsync(keyRing, SampleSettings(DnsServerEventPublisherOptions.SectionName));
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(
            state,
            DnsServerEventPublisherOptions.SectionName,
            DateTimeOffset.UtcNow,
            null
        );

        ClusterConfigSyncEnvelope envelope = Assert.Single(await ReadAsync(keyRing, state));

        Assert.Equal(
            ["rabbit-pw", "active-pw"],
            Entries(envelope)
                .Where(entry => entry.Key == "Password")
                .Select(entry => entry.Value.GetString())
        );
        Assert.DoesNotContain("PasswordEncrypted", envelope.PayloadJson);
    }

    [Fact]
    public async Task ReadBackfillAsync_ForASecretThisKeyRingCannotOpen_LeavesTheKeyOut()
    {
        await WriteFileAsync(
            new EphemeralDataProtectionProvider(),
            SampleSettings(ReverseProxyOptions.SectionName)
        );
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(state, ReverseProxyOptions.SectionName, DateTimeOffset.UtcNow, null);
        CapturingLoggerProvider logs = new();

        // A different key ring cannot open what the file holds.
        ClusterConfigSyncEnvelope envelope = Assert.Single(
            await ReadAsync(new EphemeralDataProtectionProvider(), state, logs)
        );

        List<SettingsSyncEntry> entries = Entries(envelope);
        Assert.DoesNotContain(entries, entry => entry.Key == "SharedSecret");
        Assert.Contains(entries, entry => entry.Key == "SharedSecretHeaderName");
        Assert.Contains(
            logs.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("DnsServer:ReverseProxy:SharedSecret")
        );
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("s3cret"));
    }

    [Fact]
    public async Task ReadBackfillAsync_ForAClearedSecret_SendsAnEmptyValue()
    {
        IDataProtectionProvider keyRing = new EphemeralDataProtectionProvider();
        await WriteFileAsync(
            keyRing,
            [new PresetConfigurationSetting(ReverseProxyOptions.SectionName, "SharedSecret", null)]
        );
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(state, ReverseProxyOptions.SectionName, DateTimeOffset.UtcNow, null);

        ClusterConfigSyncEnvelope envelope = Assert.Single(await ReadAsync(keyRing, state));

        // The clear is part of the section's state, so the peer clears its own copy too.
        Assert.Equal(
            string.Empty,
            Entries(envelope).Single(entry => entry.Key == "SharedSecret").Value.GetString()
        );
    }

    [Fact]
    public async Task ReadBackfillAsync_ForAValuePinnedByAnEnvironmentVariable_SendsTheFileValue()
    {
        IDataProtectionProvider keyRing = new EphemeralDataProtectionProvider();
        await WriteFileAsync(
            keyRing,
            [new PresetConfigurationSetting(DnsCookieOptions.SectionName, "Require", false)]
        );
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(state, DnsCookieOptions.SectionName, DateTimeOffset.UtcNow, null);

        // The environment pins Require to true on this node. The prefix is unique to this test, so
        // no other test or host reads the variable.
        string prefix = $"NINLILTEST{Guid.NewGuid():N}_";
        Environment.SetEnvironmentVariable($"{prefix}DnsServer__Cookies__Require", "true");

        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(filePath)
                .AddEnvironmentVariables(prefix)
                .Build();
            // Premise: the bound configuration reports the pinned value, not the file's.
            Assert.True(
                configuration
                    .GetSection(DnsCookieOptions.SectionName)
                    .Get<DnsCookieOptions>()
                    .Require
            );

            ClusterConfigSyncEnvelope envelope = Assert.Single(await ReadAsync(keyRing, state));

            Assert.False(
                Entries(envelope).Single(entry => entry.Key == "Require").Value.GetBoolean()
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{prefix}DnsServer__Cookies__Require", null);
        }
    }

    [Fact]
    public async Task ReadBackfillAsync_ForReverseProxy_IncludesTheProxyProtocolKeys()
    {
        IDataProtectionProvider keyRing = new EphemeralDataProtectionProvider();
        await WriteFileAsync(keyRing, SampleSettings(ReverseProxyOptions.SectionName));
        SettingsClusterSyncRoundTripTests.FakeStateRepository state = new();
        await AddStateAsync(state, ReverseProxyOptions.SectionName, DateTimeOffset.UtcNow, null);

        ClusterConfigSyncEnvelope envelope = Assert.Single(await ReadAsync(keyRing, state));

        List<SettingsSyncEntry> proxyProtocol =
        [
            .. Entries(envelope)
                .Where(entry => entry.SectionPath == ProxyProtocolOptions.SectionName),
        ];
        Assert.Equal(
            ["Enable", "KnownNetworks", "KnownProxies"],
            proxyProtocol.Select(e => e.Key).Order()
        );
    }

    [Fact]
    public async Task Backfill_OfASection_ProducesThePeerFileLiveSyncProduces()
    {
        // Node A: a write of every key of every section, applied to its file as the controllers
        // do and recorded through the real recorder, which captures the live payload.
        IDataProtectionProvider nodeAKeyRing = new EphemeralDataProtectionProvider();
        SettingsClusterSyncRoundTripTests.FakeStateRepository nodeAState = new();
        SettingsClusterSyncRoundTripTests.CapturingPublisher publisher = new();
        ClusterConfigSyncSettingsRecorder recorder = new(
            nodeAState,
            new SettingsClusterSyncRoundTripTests.FakeUnitOfWork(),
            new SettingsClusterSyncRoundTripTests.FakeClusterOptionsMonitor(
                new ClusterOptions { NodeId = ownNodeId }
            ),
            publisher
        );

        IDataProtectionProvider peerKeyRing = new EphemeralDataProtectionProvider();
        SettingsClusterSyncRoundTripTests.FakeStateRepository liveState = new();
        SettingsClusterSyncRoundTripTests.FakeStateRepository backfillState = new();
        SettingsClusterConfigSyncApplier liveApplier = CreateApplier(
            liveState,
            liveFilePath,
            peerKeyRing
        );
        SettingsClusterConfigSyncApplier backfillApplier = CreateApplier(
            backfillState,
            backfillFilePath,
            peerKeyRing
        );

        foreach (ClusterSyncedSettingsSection section in ClusterSyncedSettingsCatalog.Sections)
        {
            PresetConfigurationSetting[] settings = SampleSettings(section.SectionName);
            await WriteFileAsync(nodeAKeyRing, settings);
            await recorder.RecordAndPublishAsync(
                section.SectionName,
                settings,
                CancellationToken.None
            );

            await liveApplier.ApplyAsync(
                new ClusterConfigSyncEnvelope(
                    SettingsClusterConfigSyncEntityType.EntityTypeName,
                    section.SectionName,
                    false,
                    publisher.Published.SettingsJson,
                    publisher.Published.OccurredAtUtc,
                    ownNodeId
                ),
                CancellationToken.None
            );
        }

        List<ClusterConfigSyncEnvelope> backfill = await ReadAsync(nodeAKeyRing, nodeAState);
        Assert.Equal(ClusterSyncedSettingsCatalog.Sections.Count, backfill.Count);

        foreach (ClusterConfigSyncEnvelope envelope in backfill)
            await backfillApplier.ApplyAsync(envelope, CancellationToken.None);

        // Peers encrypt a secret with their own random-IV payload, so the two files differ in the
        // ciphertext bytes. Everything else must match, and the ciphertexts must open to the same
        // plaintext.
        JsonObject live = await ReadNormalisedAsync(liveFilePath, peerKeyRing);
        JsonObject backfilled = await ReadNormalisedAsync(backfillFilePath, peerKeyRing);
        Assert.Equal(live.ToJsonString(), backfilled.ToJsonString());
        Assert.Equal(
            "s3cret",
            backfilled["DnsServer"]["ReverseProxy"]["SharedSecretEncrypted"].GetValue<string>()
        );
    }

    /// <summary>
    ///     This method returns every setting of the given sections with a sample value of the
    ///     type the matching controller writes. It lists each catalogued key explicitly, and the
    ///     guard at the end fails when the catalogue and this list disagree, so a new catalogued
    ///     key must get a sample value here. The secrets use the plain key, as a controller
    ///     passes them.
    /// </summary>
    internal static PresetConfigurationSetting[] SampleSettings(params string[] sectionNames)
    {
        string events = DnsServerEventPublisherOptions.SectionName;
        const string rabbit = "DnsServer:Events:RabbitMq";
        const string active = "DnsServer:Events:ActiveMq";
        string rate = DnsRateLimitOptions.SectionName;
        string cookies = DnsCookieOptions.SectionName;
        string proxy = ReverseProxyOptions.SectionName;
        string proxyProtocol = ProxyProtocolOptions.SectionName;
        string rewrites = DnsRewriteOptions.SectionName;

        Dictionary<string, PresetConfigurationSetting[]> all = new()
        {
            [events] =
            [
                new(events, "Transport", "RabbitMq"),
                new(rabbit, "HostName", "rabbit.example"),
                new(rabbit, "Port", 5673),
                new(rabbit, "UserName", "rabbit-user"),
                new(rabbit, "Password", "rabbit-pw"),
                new(rabbit, "VirtualHost", "/ninlil"),
                new(rabbit, "ExchangeName", "ex"),
                new(rabbit, "InitialReconnectDelay", "00:00:02"),
                new(rabbit, "MaxReconnectDelay", "00:01:00"),
                new(active, "HostName", "active.example"),
                new(active, "Port", 61617),
                new(active, "UserName", "active-user"),
                new(active, "Password", "active-pw"),
                new(active, "TopicName", "topic"),
                new(active, "InitialReconnectDelay", "00:00:03"),
                new(active, "MaxReconnectDelay", "00:02:00"),
                new(active, "PublishTimeout", "00:00:05"),
            ],
            [rate] =
            [
                new(rate, "Enable", true),
                new(rate, "QueriesPerSecond", 2.5),
                new(rate, "BurstSize", 42),
                new(rate, "TrackedClientExpiry", "00:10:00"),
                new(rate, "CleanupInterval", "00:02:00"),
                new(rate, "MaxTrackedClients", 1000),
                new(rate, "SubnetPrefixLengthIPv4", 24),
                new(rate, "SubnetPrefixLengthIPv6", 56),
                new(rate, "SubnetSampleWindow", "00:00:30"),
                new(rate, "SubnetUdpQueryLimit", 500),
                new(rate, "SubnetTcpQueryLimit", 200),
                new(rate, "SubnetTruncatePercentage", 12.5),
                new(rate, "SubnetBypassCidrs", new List<string> { "10.0.0.0/8", "fd00::/8" }),
            ],
            [cookies] =
            [
                new(cookies, "Enable", true),
                new(cookies, "Require", true),
                new(cookies, "SecretRotationInterval", "12:00:00"),
            ],
            [proxy] =
            [
                new(proxy, "Enable", true),
                new(proxy, "KnownProxies", new List<string> { "192.0.2.1", "2001:db8::1" }),
                new(proxy, "KnownNetworks", new List<string> { "198.51.100.0/24" }),
                new(proxy, "SharedSecretHeaderName", "X-Secret"),
                new(proxy, "SharedSecret", "s3cret"),
                new(proxyProtocol, "Enable", true),
                new(proxyProtocol, "KnownProxies", new List<string> { "203.0.113.5" }),
                new(proxyProtocol, "KnownNetworks", new List<string> { "172.16.0.0/16" }),
            ],
            [rewrites] = [new(rewrites, "Enable", true), new(rewrites, "TimeToLiveSeconds", 900u)],
        };

        foreach (string sectionName in all.Keys)
            Assert.Equal(
                CataloguedKeys(sectionName),
                all[sectionName].Select(setting => (setting.SectionPath, setting.Key)).Order()
            );

        Assert.Equal(
            ClusterSyncedSettingsCatalog.Sections.Select(section => section.SectionName).Order(),
            all.Keys.Order()
        );

        return [.. sectionNames.SelectMany(name => all[name])];
    }

    /// <summary>
    ///     This method returns a section's catalogued paths and keys in a fixed order.
    /// </summary>
    private static IEnumerable<(string SectionPath, string Key)> CataloguedKeys(
        string sectionName
    ) => ClusterSyncedSettingsCatalog.Find(sectionName).Keys.Order();

    private static List<SettingsSyncEntry> Entries(ClusterConfigSyncEnvelope envelope) =>
        JsonSerializer.Deserialize<List<SettingsSyncEntry>>(envelope.PayloadJson);

    private static string TempFile(string name) =>
        Path.Combine(Path.GetTempPath(), $"ninlil-settings-source-{name}-{Guid.NewGuid():N}.json");

    /// <summary>
    ///     This method writes settings to the node's file the way a settings controller does:
    ///     secrets encrypted with the node's own key ring.
    /// </summary>
    private async Task WriteFileAsync(
        IDataProtectionProvider keyRing,
        IReadOnlyList<PresetConfigurationSetting> settings
    ) =>
        await PresetConfigurationFileApplier.ApplyAsync(
            ConfigurationSecretSettings.ProtectForFile(settings, keyRing),
            filePath
        );

    /// <summary>This method adds a state row for a section.</summary>
    private static Task AddStateAsync(
        SettingsClusterSyncRoundTripTests.FakeStateRepository repository,
        string sectionName,
        DateTimeOffset writtenAt,
        Guid? originNodeId
    ) =>
        repository.AddAsync(
            new ClusterSyncedSettingsStateEntity
            {
                Id = Guid.NewGuid(),
                SectionName = sectionName,
                CreatedAtUtc = writtenAt,
                OriginatingNodeId = originNodeId,
            }
        );

    private async Task<List<ClusterConfigSyncEnvelope>> ReadAsync(
        IDataProtectionProvider keyRing,
        SettingsClusterSyncRoundTripTests.FakeStateRepository state,
        CapturingLoggerProvider logs = null
    )
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            if (logs is not null)
                builder.AddProvider(logs);
        });
        SettingsClusterConfigSyncSource source = new(
            state,
            new RuntimeConfigurationOptions { ConfigurationFilePath = filePath },
            keyRing,
            new SettingsClusterSyncRoundTripTests.FakeClusterOptionsMonitor(
                new ClusterOptions { NodeId = ownNodeId }
            ),
            loggerFactory.CreateLogger<SettingsClusterConfigSyncSource>()
        );

        List<ClusterConfigSyncEnvelope> envelopes = [];
        await foreach (
            ClusterConfigSyncEnvelope envelope in source.ReadBackfillAsync(CancellationToken.None)
        )
            envelopes.Add(envelope);

        return envelopes;
    }

    private static SettingsClusterConfigSyncApplier CreateApplier(
        SettingsClusterSyncRoundTripTests.FakeStateRepository state,
        string path,
        IDataProtectionProvider keyRing
    ) =>
        new(
            state,
            new SettingsClusterSyncRoundTripTests.FakeUnitOfWork(),
            new RuntimeConfigurationOptions { ConfigurationFilePath = path },
            keyRing,
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<SettingsClusterConfigSyncApplier>
                .Instance
        );

    /// <summary>
    ///     This method reads a peer file and replaces every encrypted secret with its plaintext, so
    ///     two files that differ only in ciphertext bytes compare equal.
    /// </summary>
    private static async Task<JsonObject> ReadNormalisedAsync(
        string path,
        IDataProtectionProvider keyRing
    )
    {
        JsonObject root = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(path));

        foreach (ConfigurationSecretSetting secret in ConfigurationSecretSettings.All)
        {
            JsonObject section = (JsonObject)
                RuntimeConfigurationFileReader.FindSection(root, secret.SectionPath);
            string payload = section?[secret.EncryptedKey]?.GetValue<string>();

            if (!string.IsNullOrEmpty(payload))
                section[secret.EncryptedKey] = ConfigurationSecretSettings.Unprotect(
                    secret,
                    keyRing,
                    payload
                );
        }

        return root;
    }

    /// <summary>
    ///     This class is a logger provider that records every entry, so a test can assert on the
    ///     warning the source logs.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        /// <summary>This property contains the recorded entries, oldest first.</summary>
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        /// <inheritdoc />
        public void Dispose() { }

        /// <summary>This class is the logger that adds each entry to the provider's list.</summary>
        /// <param name="entries">The list shared with the provider.</param>
        private sealed class CapturingLogger(List<(LogLevel Level, string Message)> entries)
            : ILogger
        {
            /// <inheritdoc />
            public IDisposable BeginScope<TState>(TState state) => null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) => true;

            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter
            ) => entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
