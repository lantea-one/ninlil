using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Dns.Server.Rewrites.SafeSearch;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the cluster sync of the <c>DnsServer:Rewrites</c> settings section
///     without a web host. It builds <see cref="RewriteSettingsController" /> and
///     <see cref="SafeSearchController" /> by hand over EF Core repositories on a temporary SQLite
///     database and a capturing event publisher, and feeds the recorded event into
///     <see cref="SettingsClusterConfigSyncApplier" /> on a second temporary file that stands in
///     for a peer. A last test binds an <see cref="IOptionsMonitor{TOptions}" /> to a reloading
///     file to show the peer's engine switch takes effect with no restart.
/// </summary>
public sealed class RewriteSettingsClusterSyncTests : IDisposable
{
    private static readonly Guid NodeId = Guid.Parse("dddddddd-0000-0000-0000-000000000000");

    private readonly string _directory = Directory
        .CreateTempSubdirectory("ninlil-web-rewrite-settings-sync-")
        .FullName;

    private readonly string _databasePath;
    private readonly NinlilDnsContext _context;
    private readonly DnsRewriteRepository _rewrites;
    private readonly DnsClientRepository _clients;
    private readonly DnsClientGroupRepository _groups;
    private readonly ClusterSyncedSettingsStateRepository _states;
    private readonly EntityFrameworkUnitOfWork _unitOfWork;
    private readonly CapturingPublisher _publisher = new();
    private readonly SettingsClusterSyncRoundTripTests.FakeClusterOptionsMonitor _clusterOptions =
        new(new ClusterOptions { NodeId = NodeId });

    public RewriteSettingsClusterSyncTests()
    {
        _databasePath = Path.Combine(_directory, "db.sqlite3");

        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");

        _context = new NinlilDnsContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _rewrites = new DnsRewriteRepository(_context);
        _clients = new DnsClientRepository(_context);
        _groups = new DnsClientGroupRepository(_context);
        _states = new ClusterSyncedSettingsStateRepository(_context);
        _unitOfWork = new EntityFrameworkUnitOfWork(_context);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temporary file is harmless.
        }
    }

    private string LocalFilePath => Path.Combine(_directory, "local.json");

    private string PeerFilePath => Path.Combine(_directory, "peer.json");

    private ClusterConfigSyncSettingsRecorder CreateRecorder() =>
        new(_states, _unitOfWork, _clusterOptions, _publisher);

    private RewriteSettingsController CreateRewriteSettings(DnsRewriteOptions options = null) =>
        new(
            new FixedOptionsMonitor<DnsRewriteOptions>(options ?? new DnsRewriteOptions()),
            new RuntimeConfigurationOptions { ConfigurationFilePath = LocalFilePath },
            CreateRecorder(),
            _states
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().BuildServiceProvider(),
                },
            },
        };

    private SafeSearchController CreateSafeSearch(bool engineEnabled) =>
        new(
            _rewrites,
            new FixedOptionsMonitor<DnsRewriteOptions>(
                new DnsRewriteOptions { Enable = engineEnabled }
            ),
            new RuntimeConfigurationOptions { ConfigurationFilePath = LocalFilePath },
            _unitOfWork,
            _clients,
            _groups,
            _publisher,
            _clusterOptions,
            CreateRecorder()
        );

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private ClusterConfigSettingsChangedEvent SingleSettingsEvent() =>
        Assert.IsType<ClusterConfigSettingsChangedEvent>(
            Assert.Single(_publisher.Events.OfType<ClusterConfigSettingsChangedEvent>())
        );

    /// <summary>
    ///     This method applies a settings event to the peer file the way the cluster channel
    ///     delivers it: the section name is the entity key and the origin is the sender's node.
    /// </summary>
    private async Task ApplyOnPeerAsync(
        SettingsClusterSyncRoundTripTests.FakeStateRepository peerStates,
        ClusterConfigSettingsChangedEvent domainEvent,
        DateTimeOffset lastModifiedUtc
    )
    {
        SettingsClusterConfigSyncApplier applier = new(
            peerStates,
            new SettingsClusterSyncRoundTripTests.FakeUnitOfWork(),
            new RuntimeConfigurationOptions { ConfigurationFilePath = PeerFilePath },
            new EphemeralDataProtectionProvider(),
            NullLogger<SettingsClusterConfigSyncApplier>.Instance
        );

        await applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                SettingsClusterConfigSyncEntityType.EntityTypeName,
                domainEvent.SectionName,
                false,
                domainEvent.SettingsJson,
                lastModifiedUtc,
                NodeId
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Patch_WithBothFields_PublishesOneSettingsEventAndRecordsThisNodeAsOrigin()
    {
        await CreateRewriteSettings().Patch(Json("""{"enable": true, "timeToLiveSeconds": 120}"""));

        ClusterConfigSettingsChangedEvent published = SingleSettingsEvent();
        Assert.Equal(DnsRewriteOptions.SectionName, published.SectionName);

        List<SettingsSyncEntry> entries = JsonSerializer.Deserialize<List<SettingsSyncEntry>>(
            published.SettingsJson
        );
        Assert.Equal(2, entries.Count);
        Assert.All(
            entries,
            entry => Assert.Equal(DnsRewriteOptions.SectionName, entry.SectionPath)
        );
        Assert.Equal(
            JsonValueKind.True,
            entries.Single(entry => entry.Key == nameof(DnsRewriteOptions.Enable)).Value.ValueKind
        );
        JsonElement ttl = entries
            .Single(entry => entry.Key == nameof(DnsRewriteOptions.TimeToLiveSeconds))
            .Value;
        Assert.Equal(JsonValueKind.Number, ttl.ValueKind);
        Assert.Equal(120, ttl.GetInt32());

        ClusterSyncedSettingsStateEntity state = await _states.FindBySectionNameAsync(
            DnsRewriteOptions.SectionName
        );
        Assert.NotNull(state);
        Assert.Equal(NodeId, state.OriginatingNodeId);

        // GET reports the same bookkeeping.
        RewriteSettingsDetail detail = Assert.IsType<RewriteSettingsDetail>(
            Assert
                .IsType<OkObjectResult>(
                    (await CreateRewriteSettings().Get(CancellationToken.None)).Result
                )
                .Value
        );
        Assert.Equal(NodeId, detail.OriginatingNodeId);
        Assert.NotNull(detail.LastModifiedUtc);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_PublishesNothingAndRecordsNoState()
    {
        await CreateRewriteSettings().Patch(Json("{}"));

        Assert.Empty(_publisher.Events);
        Assert.Null(await _states.FindBySectionNameAsync(DnsRewriteOptions.SectionName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SafeSearchEnable_WithTheEngineOff_PublishesTheSwitchAsASettingsEvent(
        bool clientScoped
    )
    {
        Guid? clientId = null;
        if (clientScoped)
        {
            DnsClientEntity client = new()
            {
                Id = Guid.NewGuid(),
                DisplayName = "c",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            await _clients.AddAsync(client);
            await _unitOfWork.SaveChangesAsync();
            clientId = client.Id;
        }

        await CreateSafeSearch(engineEnabled: false)
            .Enable(SafeSearchCatalog.Entries[0].Key, clientId, null, default);

        List<SettingsSyncEntry> entries = JsonSerializer.Deserialize<List<SettingsSyncEntry>>(
            SingleSettingsEvent().SettingsJson
        );
        SettingsSyncEntry entry = Assert.Single(entries);
        Assert.Equal(DnsRewriteOptions.SectionName, entry.SectionPath);
        Assert.Equal(nameof(DnsRewriteOptions.Enable), entry.Key);
        Assert.Equal(JsonValueKind.True, entry.Value.ValueKind);
        Assert.Equal(DnsRewriteOptions.SectionName, SingleSettingsEvent().SectionName);
    }

    [Fact]
    public async Task SafeSearchEnable_WithTheEngineAlreadyOn_PublishesNoSettingsEvent()
    {
        await CreateSafeSearch(engineEnabled: true)
            .Enable(SafeSearchCatalog.Entries[0].Key, null, null, default);

        Assert.Empty(_publisher.Events.OfType<ClusterConfigSettingsChangedEvent>());
        Assert.False(File.Exists(LocalFilePath));
    }

    [Fact]
    public async Task RoundTrip_OfTheRecordedEvent_SetsBothFieldsOnThePeerAndDropsAnOlderWrite()
    {
        await File.WriteAllTextAsync(
            PeerFilePath,
            """{"DnsServer":{"Rewrites":{"Enable":false}}}"""
        );
        await CreateRewriteSettings().Patch(Json("""{"enable": true, "timeToLiveSeconds": 120}"""));
        ClusterConfigSettingsChangedEvent first = SingleSettingsEvent();
        SettingsClusterSyncRoundTripTests.FakeStateRepository peerStates = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await ApplyOnPeerAsync(peerStates, first, now);

        JsonObject section = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(PeerFilePath))["DnsServer"]["Rewrites"];
        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal(120, section["TimeToLiveSeconds"].GetValue<int>());

        // An older write for the same section is dropped by last-write-wins.
        ClusterConfigSettingsChangedEvent older = new(
            now.AddMinutes(-1),
            DnsRewriteOptions.SectionName,
            """[{"SectionPath":"DnsServer:Rewrites","Key":"Enable","Value":false}]"""
        );
        await ApplyOnPeerAsync(peerStates, older, now.AddMinutes(-1));

        section = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(PeerFilePath))["DnsServer"]["Rewrites"];
        Assert.True(section["Enable"].GetValue<bool>());
    }

    /// <summary>
    ///     The peer's engine reads <see cref="IOptionsMonitor{TOptions}" />, so the switch must
    ///     show up on a reloading configuration file after the applier writes it.
    /// </summary>
    [Fact]
    public async Task Apply_OnAPeerWithTheEngineOff_TurnsTheOptionsMonitorOnWithNoRestart()
    {
        await File.WriteAllTextAsync(
            PeerFilePath,
            """{"DnsServer":{"Rewrites":{"Enable":false}}}"""
        );
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(PeerFilePath, optional: false, reloadOnChange: true)
            .Build();
        ServiceCollection services = new();
        services.Configure<DnsRewriteOptions>(
            configuration.GetSection(DnsRewriteOptions.SectionName)
        );
        await using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<DnsRewriteOptions> monitor = provider.GetRequiredService<
            IOptionsMonitor<DnsRewriteOptions>
        >();
        Assert.False(monitor.CurrentValue.Enable);

        await CreateRewriteSettings().Patch(Json("""{"enable": true}"""));
        await ApplyOnPeerAsync(
            new SettingsClusterSyncRoundTripTests.FakeStateRepository(),
            SingleSettingsEvent(),
            DateTimeOffset.UtcNow
        );

        // The JSON provider debounces file-change notifications, so wait for the written value
        // itself, as QueryLoggingOptionsHotReloadTests does.
        for (int attempt = 0; attempt < 200 && !monitor.CurrentValue.Enable; attempt++)
            await Task.Delay(50);

        Assert.True(monitor.CurrentValue.Enable);
    }

    /// <summary>This class is an options monitor that always returns one value.</summary>
    private sealed class FixedOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => value;

        public TOptions Get(string name) => value;

        public IDisposable OnChange(Action<TOptions, string> listener) => null;
    }

    /// <summary>This class collects every event published to it.</summary>
    private sealed class CapturingPublisher : IDnsServerEventPublisher
    {
        public List<IDnsServerEvent> Events { get; } = [];

        public void Publish(IDnsServerEvent domainEvent) => Events.Add(domainEvent);
    }
}
