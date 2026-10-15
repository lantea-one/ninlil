using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Controllers;
using Ninlil.Web.Middleware;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Ninlil.Web.Startup;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how the reverse-proxy shared secret is stored and replicated, without a
///     web host, over a temporary configuration file and
///     <see cref="EphemeralDataProtectionProvider" />s that stand in for each node's key ring. It
///     checks the four places the secret passes through: the settings controller (the file gets
///     only the encrypted value, the cluster payload gets the plaintext), the cluster applier on a
///     peer (re-encrypts with the peer's own key ring), the startup resolution (plaintext wins,
///     an unreadable secret fails closed) and the startup migration of a plaintext secret already
///     in the file.
/// </summary>
public sealed class ConfigurationSecretReverseProxyTests : IDisposable
{
    /// <summary>This field contains the temporary directory that holds the configuration file under test.</summary>
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-secret-rp-{Guid.NewGuid():N}"
    );

    /// <summary>
    ///     This field contains the key ring of the node under test. A second, separate
    ///     <see cref="EphemeralDataProtectionProvider" /> stands in for a peer or for a replaced
    ///     key ring: it cannot open what this one protected.
    /// </summary>
    private readonly EphemeralDataProtectionProvider nodeKeyRing = new();

    /// <summary>This constructor creates the temporary directory.</summary>
    public ConfigurationSecretReverseProxyTests() => Directory.CreateDirectory(directory);

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(directory, recursive: true);

    /// <summary>This property contains the path of the configuration file under test.</summary>
    private string FilePath => Path.Combine(directory, "appsettings.json");

    /// <summary>This method decrypts a stored secret payload with the given node's key ring.</summary>
    private string Decrypt(IDataProtectionProvider keyRing, string payload) =>
        keyRing
            .CreateProtector(ReverseProxyOptions.SharedSecretProtectorPurpose)
            .Unprotect(payload);

    /// <summary>This method reads the <c>DnsServer:ReverseProxy</c> section from the file as written to disk.</summary>
    private JsonObject ReadProxySection() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(FilePath))["DnsServer"]["ReverseProxy"];

    /// <summary>
    ///     This method patches the controller (constructed directly over fakes) and returns the
    ///     sync payload it recorded.
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
        ReverseProxySettingsController controller = new(
            MicrosoftOptions.Create(new ReverseProxyOptions()),
            MicrosoftOptions.Create(new DnsOverTlsOptions()),
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
    private async Task ApplyOnPeerAsync(string payloadJson, IDataProtectionProvider peerKeyRing)
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
                ReverseProxyOptions.SectionName,
                false,
                payloadJson,
                DateTimeOffset.UtcNow,
                Guid.NewGuid()
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Patch_WithASharedSecret_StoresItEncryptedAndSendsThePlaintextToPeers()
    {
        string payload = await PatchAsync("""{"sharedSecret":"s3cret-value"}""");

        // Threat: a copy of the file or of a backup must not reveal the secret.
        string text = await File.ReadAllTextAsync(FilePath);
        Assert.DoesNotContain("s3cret-value", text);

        JsonObject section = ReadProxySection();
        Assert.Equal("", section["SharedSecret"].GetValue<string>());
        Assert.Equal(
            "s3cret-value",
            Decrypt(nodeKeyRing, section["SharedSecretEncrypted"].GetValue<string>())
        );

        // The peer can open only what its own key ring protected, so it needs the plaintext.
        List<SettingsSyncEntry> entries = JsonSerializer.Deserialize<List<SettingsSyncEntry>>(
            payload
        );
        SettingsSyncEntry secretEntry = Assert.Single(entries, e => e.Key == "SharedSecret");
        Assert.Equal("s3cret-value", secretEntry.Value.GetString());
        Assert.DoesNotContain(entries, e => e.Key == "SharedSecretEncrypted");
    }

    [Fact]
    public async Task Patch_WithANullSharedSecret_ClearsBothKeysAndTellsPeersToClear()
    {
        await PatchAsync("""{"sharedSecret":"s3cret-value"}""");

        string payload = await PatchAsync("""{"sharedSecret":null}""");

        JsonObject section = ReadProxySection();
        Assert.Equal("", section["SharedSecret"]?.GetValue<string>() ?? "");
        Assert.Equal("", section["SharedSecretEncrypted"].GetValue<string>());

        List<SettingsSyncEntry> entries = JsonSerializer.Deserialize<List<SettingsSyncEntry>>(
            payload
        );
        Assert.Equal(
            JsonValueKind.Null,
            Assert.Single(entries, e => e.Key == "SharedSecret").Value.ValueKind
        );
    }

    [Fact]
    public async Task Patch_WithoutASharedSecret_LeavesTheEncryptedSecretAlone()
    {
        await PatchAsync("""{"sharedSecret":"s3cret-value"}""");
        string before = ReadProxySection()["SharedSecretEncrypted"].GetValue<string>();

        await PatchAsync("""{"sharedSecretHeaderName":"X-Renamed"}""");

        JsonObject section = ReadProxySection();
        Assert.Equal(before, section["SharedSecretEncrypted"].GetValue<string>());
        Assert.Equal("X-Renamed", section["SharedSecretHeaderName"].GetValue<string>());
    }

    [Fact]
    public async Task Apply_OnAPeer_EncryptsTheSecretWithThePeersOwnKeyRing()
    {
        EphemeralDataProtectionProvider peerKeyRing = new();
        string payload = JsonSerializer.Serialize(
            new List<SettingsSyncEntry>
            {
                new(
                    ReverseProxyOptions.SectionName,
                    "SharedSecret",
                    JsonSerializer.SerializeToElement("peer-secret")
                ),
                new(
                    ReverseProxyOptions.SectionName,
                    "Enable",
                    JsonSerializer.SerializeToElement(true)
                ),
            }
        );

        await ApplyOnPeerAsync(payload, peerKeyRing);

        Assert.DoesNotContain("peer-secret", await File.ReadAllTextAsync(FilePath));
        JsonObject section = ReadProxySection();
        Assert.Equal("", section["SharedSecret"].GetValue<string>());
        Assert.Equal(
            "peer-secret",
            Decrypt(peerKeyRing, section["SharedSecretEncrypted"].GetValue<string>())
        );
        Assert.True(section["Enable"].GetValue<bool>());

        // Another node's key ring cannot open it.
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            Decrypt(
                new EphemeralDataProtectionProvider(),
                section["SharedSecretEncrypted"].GetValue<string>()
            )
        );
    }

    [Fact]
    public async Task Apply_OfAnEncryptedValueFromAnotherNode_IsNotWrittenToTheFile()
    {
        string payload = JsonSerializer.Serialize(
            new List<SettingsSyncEntry>
            {
                new(
                    ReverseProxyOptions.SectionName,
                    "SharedSecretEncrypted",
                    JsonSerializer.SerializeToElement("CfDJ8-not-readable-here")
                ),
                new(
                    ReverseProxyOptions.SectionName,
                    "Enable",
                    JsonSerializer.SerializeToElement(true)
                ),
            }
        );

        await ApplyOnPeerAsync(payload, nodeKeyRing);

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
    ///     This method builds the options the way <c>Program.cs</c> does: the configuration
    ///     section is bound, then the secret resolution post-configures it.
    /// </summary>
    private static ReverseProxyOptions Resolve(
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
        services.Configure<ReverseProxyOptions>(
            configuration.GetSection(ReverseProxyOptions.SectionName)
        );
        services.AddNinlilConfigurationSecretResolution(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;
    }

    [Fact]
    public void Resolve_WithOnlyAnEncryptedSecret_DecryptsItIntoSharedSecret()
    {
        string encrypted = nodeKeyRing
            .CreateProtector(ReverseProxyOptions.SharedSecretProtectorPurpose)
            .Protect("stored-secret");

        ReverseProxyOptions options = Resolve(
            nodeKeyRing,
            new CapturingLoggerProvider(),
            new Dictionary<string, string>
            {
                [$"{ReverseProxyOptions.SectionName}:SharedSecretEncrypted"] = encrypted,
            }
        );

        Assert.Equal("stored-secret", options.SharedSecret);
    }

    [Fact]
    public void Resolve_WithAPlaintextAndAnEncryptedSecret_KeepsThePlaintext()
    {
        string encrypted = nodeKeyRing
            .CreateProtector(ReverseProxyOptions.SharedSecretProtectorPurpose)
            .Protect("stored-secret");

        ReverseProxyOptions options = Resolve(
            nodeKeyRing,
            new CapturingLoggerProvider(),
            new Dictionary<string, string>
            {
                [$"{ReverseProxyOptions.SectionName}:SharedSecret"] = "from-environment",
                [$"{ReverseProxyOptions.SectionName}:SharedSecretEncrypted"] = encrypted,
            }
        );

        Assert.Equal("from-environment", options.SharedSecret);
    }

    [Fact]
    public void Resolve_WithNothingConfigured_LeavesNoSecret()
    {
        ReverseProxyOptions options = Resolve(
            nodeKeyRing,
            new CapturingLoggerProvider(),
            new Dictionary<string, string>
            {
                [$"{ReverseProxyOptions.SectionName}:Enable"] = "true",
                [$"{ReverseProxyOptions.SectionName}:SharedSecret"] = "",
                [$"{ReverseProxyOptions.SectionName}:SharedSecretEncrypted"] = "",
            }
        );

        Assert.True(string.IsNullOrEmpty(options.SharedSecret));
    }

    [Fact]
    public async Task Resolve_WithAnUndecryptableSecret_LogsAnErrorAndFailsClosed()
    {
        string encryptedByAnotherNode = new EphemeralDataProtectionProvider()
            .CreateProtector(ReverseProxyOptions.SharedSecretProtectorPurpose)
            .Protect("original-secret");
        CapturingLoggerProvider logs = new();

        ReverseProxyOptions options = Resolve(
            nodeKeyRing,
            logs,
            new Dictionary<string, string>
            {
                [$"{ReverseProxyOptions.SectionName}:Enable"] = "true",
                [$"{ReverseProxyOptions.SectionName}:SharedSecretEncrypted"] =
                    encryptedByAnotherNode,
            }
        );

        Assert.Contains(
            logs.Entries,
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("SharedSecret")
        );
        Assert.False(string.IsNullOrEmpty(options.SharedSecret));
        Assert.NotEqual("original-secret", options.SharedSecret);

        // Threat: falling back to "no secret" would let any request from an allowlisted address
        // through. No proxy is trusted: a request that presents the original secret from an allowlisted
        // address still has its forwarded headers removed.
        options.KnownProxies = ["203.0.113.10"];
        ForwardedHeadersOptions forwardedHeadersOptions = new();
        ReverseProxyForwardedHeadersConfigurator.Configure(
            forwardedHeadersOptions,
            options,
            NullLogger.Instance
        );
        ReverseProxyTrustGatingMiddleware middleware = new(
            _ => Task.CompletedTask,
            MicrosoftOptions.Create(forwardedHeadersOptions),
            MicrosoftOptions.Create(options)
        );
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.7";
        context.Request.Headers[ReverseProxyOptions.DefaultSharedSecretHeaderName] =
            "original-secret";

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey("X-Forwarded-For"));
    }

    /// <summary>This method writes the given JSON as the configuration file and returns its path.</summary>
    private string WriteFile(string json)
    {
        File.WriteAllText(FilePath, json);
        return FilePath;
    }

    [Fact]
    public async Task Migrate_OfAPlaintextSecret_WritesTheEncryptedKeyAndEmptiesThePlaintext()
    {
        string path = WriteFile(
            """{"DnsServer":{"ReverseProxy":{"Enable":true,"SharedSecret":"old-plaintext"}},"Other":{"Key":"v"}}"""
        );

        await ConfigurationSecretMigrationService.MigrateAsync(
            path,
            nodeKeyRing,
            NullLogger.Instance
        );

        string text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("old-plaintext", text);
        JsonObject section = ReadProxySection();
        Assert.Equal("", section["SharedSecret"].GetValue<string>());
        Assert.Equal(
            "old-plaintext",
            Decrypt(nodeKeyRing, section["SharedSecretEncrypted"].GetValue<string>())
        );
        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal("v", JsonNode.Parse(text)["Other"]["Key"].GetValue<string>());
    }

    [Fact]
    public async Task Migrate_RunTwice_ChangesNothingTheSecondTime()
    {
        string path = WriteFile(
            """{"DnsServer":{"ReverseProxy":{"SharedSecret":"old-plaintext"}}}"""
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

    [Theory]
    [InlineData("""{"DnsServer":{"ReverseProxy":{"SharedSecret":""}}}""")]
    [InlineData("""{"DnsServer":{"ReverseProxy":{"SharedSecret":null}}}""")]
    [InlineData("""{"DnsServer":{"ReverseProxy":{"Enable":true}}}""")]
    [InlineData("""{"Other":{}}""")]
    public async Task Migrate_WithNoPlaintextSecret_LeavesTheFileUntouched(string json)
    {
        string path = WriteFile(json);

        await ConfigurationSecretMigrationService.MigrateAsync(
            path,
            nodeKeyRing,
            NullLogger.Instance
        );

        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Migrate_WithAMissingFile_DoesNotCreateIt()
    {
        string path = Path.Combine(directory, "missing.json");

        await ConfigurationSecretMigrationService.MigrateAsync(
            path,
            nodeKeyRing,
            NullLogger.Instance
        );

        Assert.False(File.Exists(path));
    }
}
