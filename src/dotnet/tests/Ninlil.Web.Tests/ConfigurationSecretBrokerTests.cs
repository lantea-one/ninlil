using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Events.ActiveMq;
using Ninlil.Dns.Server.Events.RabbitMq;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Ninlil.Web.Startup;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how the RabbitMQ and ActiveMQ broker passwords are stored and
///     replicated, without a web host, over a temporary configuration file and
///     <see cref="EphemeralDataProtectionProvider" />s that stand in for each node's key ring. It
///     mirrors <see cref="ConfigurationSecretReverseProxyTests" /> for the four places a password
///     passes through (settings controller, cluster applier on a peer, startup resolution,
///     startup migration). It differs where the broker rules differ: an unreadable password
///     becomes an empty one, and with nothing configured the class defaults stay. Each test runs
///     once per broker (<c>RabbitMq</c> and <c>ActiveMq</c>).
/// </summary>
public sealed class ConfigurationSecretBrokerTests : IDisposable
{
    /// <summary>This field contains the temporary directory that holds the configuration file under test.</summary>
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-secret-broker-{Guid.NewGuid():N}"
    );

    /// <summary>
    ///     This field contains the key ring of the node under test. A second, separate
    ///     <see cref="EphemeralDataProtectionProvider" /> stands in for a peer or for a replaced
    ///     key ring.
    /// </summary>
    private readonly EphemeralDataProtectionProvider nodeKeyRing = new();

    /// <summary>This constructor creates the temporary directory.</summary>
    public ConfigurationSecretBrokerTests() => Directory.CreateDirectory(directory);

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(directory, recursive: true);

    /// <summary>This property contains the path of the configuration file under test.</summary>
    private string FilePath => Path.Combine(directory, "appsettings.json");

    /// <summary>This method returns the configuration section path of a broker.</summary>
    private static string SectionOf(string broker) =>
        broker == "RabbitMq"
            ? RabbitMqEventBrokerOptions.SectionName
            : ActiveMqEventBrokerOptions.SectionName;

    /// <summary>This method returns the Data Protection purpose of a broker's password.</summary>
    private static string PurposeOf(string broker) =>
        broker == "RabbitMq"
            ? RabbitMqEventBrokerOptions.PasswordProtectorPurpose
            : ActiveMqEventBrokerOptions.PasswordProtectorPurpose;

    /// <summary>This method returns the JSON name of a broker's sub-object in the settings patch.</summary>
    private static string PatchNameOf(string broker) =>
        broker == "RabbitMq" ? "rabbitMq" : "activeMq";

    /// <summary>This method decrypts a stored password payload with the given node's key ring.</summary>
    private static string Decrypt(string broker, IDataProtectionProvider keyRing, string payload) =>
        keyRing.CreateProtector(PurposeOf(broker)).Unprotect(payload);

    /// <summary>This method reads the broker's section from the file as written to disk.</summary>
    private JsonObject ReadSection(string broker)
    {
        JsonNode node = JsonNode.Parse(File.ReadAllText(FilePath));

        foreach (string part in SectionOf(broker).Split(':'))
            node = node[part];

        return (JsonObject)node;
    }

    /// <summary>
    ///     This method patches the controller (constructed directly over fakes) with a password
    ///     for the given broker and returns the sync payload it recorded.
    /// </summary>
    private async Task<string> PatchAsync(string json)
    {
        SettingsClusterSyncRoundTripTests.CapturingPublisher publisher = new();
        ClusterConfigSyncSettingsRecorder recorder = new(
            new SettingsClusterSyncRoundTripTests.FakeStateRepository(),
            new SettingsClusterSyncRoundTripTests.FakeUnitOfWork(),
            new SettingsClusterSyncRoundTripTests.FakeClusterOptionsMonitor(
                new ClusterOptions { NodeId = Guid.NewGuid() }
            ),
            publisher
        );
        EventBrokerSettingsController controller = new(
            new ServiceCollection().BuildServiceProvider(),
            MicrosoftOptions.Create(new DnsServerEventPublisherOptions()),
            MicrosoftOptions.Create(new RabbitMqEventBrokerOptions()),
            MicrosoftOptions.Create(new ActiveMqEventBrokerOptions()),
            new RuntimeConfigurationOptions { ConfigurationFilePath = FilePath },
            recorder,
            new SettingsClusterSyncRoundTripTests.FakeStateRepository(),
            nodeKeyRing
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        using JsonDocument patch = JsonDocument.Parse(json);
        ActionResult<RuntimeSettingsWriteResult> result = await controller.Patch(patch.RootElement);
        Assert.IsType<OkObjectResult>(result.Result);

        return publisher.Published.SettingsJson;
    }

    /// <summary>This method applies a payload to the peer file as a node with the given key ring.</summary>
    private async Task ApplyOnPeerAsync(
        string broker,
        string payloadJson,
        IDataProtectionProvider peerKeyRing
    )
    {
        SettingsClusterConfigSyncApplier applier = new(
            new SettingsClusterSyncRoundTripTests.FakeStateRepository(),
            new SettingsClusterSyncRoundTripTests.FakeUnitOfWork(),
            new RuntimeConfigurationOptions { ConfigurationFilePath = FilePath },
            peerKeyRing,
            NullLogger<SettingsClusterConfigSyncApplier>.Instance
        );

        await applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                SettingsClusterConfigSyncEntityType.EntityTypeName,
                DnsServerEventPublisherOptions.SectionName,
                false,
                payloadJson,
                DateTimeOffset.UtcNow,
                Guid.NewGuid()
            ),
            CancellationToken.None
        );
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public async Task Patch_WithAPassword_StoresItEncryptedAndSendsThePlaintextToPeers(
        string broker
    )
    {
        string payload = await PatchAsync(
            $$$"""{"{{{PatchNameOf(broker)}}}":{"hostName":"mq.example","password":"s3cret-value"}}"""
        );

        // Threat: a copy of the file or of a backup must not reveal the password.
        Assert.DoesNotContain("s3cret-value", await File.ReadAllTextAsync(FilePath));

        JsonObject section = ReadSection(broker);
        Assert.Equal("", section["Password"].GetValue<string>());
        Assert.Equal(
            "s3cret-value",
            Decrypt(broker, nodeKeyRing, section["PasswordEncrypted"].GetValue<string>())
        );
        Assert.Equal("mq.example", section["HostName"].GetValue<string>());

        // The peer can open only what its own key ring protected, so it needs the plaintext.
        List<SettingsSyncEntry> entries = JsonSerializer.Deserialize<List<SettingsSyncEntry>>(
            payload
        );
        SettingsSyncEntry secretEntry = Assert.Single(entries, e => e.Key == "Password");
        Assert.Equal(SectionOf(broker), secretEntry.SectionPath);
        Assert.Equal("s3cret-value", secretEntry.Value.GetString());
        Assert.DoesNotContain(entries, e => e.Key == "PasswordEncrypted");
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public async Task Patch_WithANullPassword_ClearsBothKeysAndTellsPeersToClear(string broker)
    {
        await PatchAsync($$$"""{"{{{PatchNameOf(broker)}}}":{"password":"s3cret-value"}}""");

        string payload = await PatchAsync($$$"""{"{{{PatchNameOf(broker)}}}":{"password":null}}""");

        JsonObject section = ReadSection(broker);
        Assert.Equal("", section["Password"]?.GetValue<string>() ?? "");
        Assert.Equal("", section["PasswordEncrypted"].GetValue<string>());

        List<SettingsSyncEntry> entries = JsonSerializer.Deserialize<List<SettingsSyncEntry>>(
            payload
        );
        Assert.Equal(
            JsonValueKind.Null,
            Assert.Single(entries, e => e.Key == "Password").Value.ValueKind
        );
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public async Task Patch_WithoutAPassword_LeavesTheEncryptedPasswordAlone(string broker)
    {
        await PatchAsync($$$"""{"{{{PatchNameOf(broker)}}}":{"password":"s3cret-value"}}""");
        string before = ReadSection(broker)["PasswordEncrypted"].GetValue<string>();

        await PatchAsync($$$"""{"{{{PatchNameOf(broker)}}}":{"hostName":"other.example"}}""");

        JsonObject section = ReadSection(broker);
        Assert.Equal(before, section["PasswordEncrypted"].GetValue<string>());
        Assert.Equal("other.example", section["HostName"].GetValue<string>());
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public async Task Apply_OnAPeer_EncryptsThePasswordWithThePeersOwnKeyRing(string broker)
    {
        EphemeralDataProtectionProvider peerKeyRing = new();
        string payload = JsonSerializer.Serialize(
            new List<SettingsSyncEntry>
            {
                new(
                    SectionOf(broker),
                    "Password",
                    JsonSerializer.SerializeToElement("peer-secret")
                ),
                new(SectionOf(broker), "Port", JsonSerializer.SerializeToElement(5999)),
            }
        );

        await ApplyOnPeerAsync(broker, payload, peerKeyRing);

        Assert.DoesNotContain("peer-secret", await File.ReadAllTextAsync(FilePath));
        JsonObject section = ReadSection(broker);
        Assert.Equal("", section["Password"].GetValue<string>());
        Assert.Equal(
            "peer-secret",
            Decrypt(broker, peerKeyRing, section["PasswordEncrypted"].GetValue<string>())
        );
        Assert.Equal(5999, section["Port"].GetValue<int>());

        // Another node's key ring cannot open it.
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            Decrypt(
                broker,
                new EphemeralDataProtectionProvider(),
                section["PasswordEncrypted"].GetValue<string>()
            )
        );
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public async Task Apply_OfAnEncryptedValueFromAnotherNode_IsNotWrittenToTheFile(string broker)
    {
        string payload = JsonSerializer.Serialize(
            new List<SettingsSyncEntry>
            {
                new(
                    SectionOf(broker),
                    "PasswordEncrypted",
                    JsonSerializer.SerializeToElement("CfDJ8-not-readable-here")
                ),
                new(SectionOf(broker), "Port", JsonSerializer.SerializeToElement(5999)),
            }
        );

        await ApplyOnPeerAsync(broker, payload, nodeKeyRing);

        Assert.DoesNotContain("CfDJ8-not-readable-here", await File.ReadAllTextAsync(FilePath));
    }

    /// <summary>
    ///     This class is a logger provider that records every entry, so a test can assert that the
    ///     secret resolution logged an Error. It is used only by the resolution tests below.
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

    /// <summary>
    ///     This method builds a broker's password the way <c>Program.cs</c> does: the
    ///     configuration section is bound, then the secret resolution post-configures it. It
    ///     returns the resolved <c>Password</c>.
    /// </summary>
    private static string ResolvePassword(
        string broker,
        IDataProtectionProvider keyRing,
        CapturingLoggerProvider logs,
        Dictionary<string, string> configurationValues
    )
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        ServiceCollection services = new();
        services.AddSingleton(keyRing);
        services.AddLogging(logging => logging.AddProvider(logs));

        if (broker == "RabbitMq")
            services.Configure<RabbitMqEventBrokerOptions>(
                configuration.GetSection(RabbitMqEventBrokerOptions.SectionName)
            );
        else
            services.Configure<ActiveMqEventBrokerOptions>(
                configuration.GetSection(ActiveMqEventBrokerOptions.SectionName)
            );

        services.AddNinlilConfigurationSecretResolution(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();

        return broker == "RabbitMq"
            ? provider.GetRequiredService<IOptions<RabbitMqEventBrokerOptions>>().Value.Password
            : provider.GetRequiredService<IOptions<ActiveMqEventBrokerOptions>>().Value.Password;
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public void Resolve_WithOnlyAnEncryptedPassword_DecryptsItIntoPassword(string broker)
    {
        string encrypted = nodeKeyRing.CreateProtector(PurposeOf(broker)).Protect("stored-secret");

        string password = ResolvePassword(
            broker,
            nodeKeyRing,
            new CapturingLoggerProvider(),
            new Dictionary<string, string>
            {
                [$"{SectionOf(broker)}:PasswordEncrypted"] = encrypted,
            }
        );

        Assert.Equal("stored-secret", password);
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public void Resolve_WithAPlaintextAndAnEncryptedPassword_KeepsThePlaintext(string broker)
    {
        string encrypted = nodeKeyRing.CreateProtector(PurposeOf(broker)).Protect("stored-secret");

        string password = ResolvePassword(
            broker,
            nodeKeyRing,
            new CapturingLoggerProvider(),
            new Dictionary<string, string>
            {
                [$"{SectionOf(broker)}:Password"] = "from-environment",
                [$"{SectionOf(broker)}:PasswordEncrypted"] = encrypted,
            }
        );

        Assert.Equal("from-environment", password);
    }

    [Theory]
    [InlineData("RabbitMq", "guest")]
    [InlineData("ActiveMq", "admin")]
    public void Resolve_WithNothingConfigured_KeepsTheClassDefault(
        string broker,
        string expectedDefault
    )
    {
        string password = ResolvePassword(
            broker,
            nodeKeyRing,
            new CapturingLoggerProvider(),
            new Dictionary<string, string> { [$"{SectionOf(broker)}:HostName"] = "mq.example" }
        );

        Assert.Equal(expectedDefault, password);
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public void Resolve_WithAnUndecryptablePassword_LogsAnErrorAndUsesAnEmptyPassword(string broker)
    {
        string encryptedByAnotherNode = new EphemeralDataProtectionProvider()
            .CreateProtector(PurposeOf(broker))
            .Protect("original-secret");
        CapturingLoggerProvider logs = new();

        string password = ResolvePassword(
            broker,
            nodeKeyRing,
            logs,
            new Dictionary<string, string>
            {
                [$"{SectionOf(broker)}:PasswordEncrypted"] = encryptedByAnotherNode,
            }
        );

        Assert.Contains(
            logs.Entries,
            entry =>
                entry.Level == LogLevel.Error
                && entry.Message.Contains($"{SectionOf(broker)}:Password")
        );
        Assert.Equal("", password);
    }

    /// <summary>This method writes the given JSON as the configuration file and returns its path.</summary>
    private string WriteFile(string json)
    {
        File.WriteAllText(FilePath, json);
        return FilePath;
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public async Task Migrate_OfAPlaintextPassword_WritesTheEncryptedKeyAndEmptiesThePlaintext(
        string broker
    )
    {
        string path = WriteFile(
            $$$$"""{"DnsServer":{"Events":{"{{{{broker}}}}":{"HostName":"mq.example","Password":"old-plaintext"}}},"Other":{"Key":"v"}}"""
        );

        await ConfigurationSecretMigrationService.MigrateAsync(
            path,
            nodeKeyRing,
            NullLogger.Instance
        );

        string text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("old-plaintext", text);
        JsonObject section = ReadSection(broker);
        Assert.Equal("", section["Password"].GetValue<string>());
        Assert.Equal(
            "old-plaintext",
            Decrypt(broker, nodeKeyRing, section["PasswordEncrypted"].GetValue<string>())
        );
        Assert.Equal("mq.example", section["HostName"].GetValue<string>());
        Assert.Equal("v", JsonNode.Parse(text)["Other"]["Key"].GetValue<string>());
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData("ActiveMq")]
    public async Task Migrate_RunTwice_ChangesNothingTheSecondTime(string broker)
    {
        string path = WriteFile(
            $$$$$"""{"DnsServer":{"Events":{"{{{{{broker}}}}}":{"Password":"old-plaintext"}}}}"""
        );
        await ConfigurationSecretMigrationService.MigrateAsync(
            path,
            nodeKeyRing,
            NullLogger.Instance
        );
        string afterFirst = await File.ReadAllTextAsync(path);

        await ConfigurationSecretMigrationService.MigrateAsync(
            path,
            nodeKeyRing,
            NullLogger.Instance
        );

        Assert.Equal(afterFirst, await File.ReadAllTextAsync(path));
    }
}
