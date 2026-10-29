using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Dns.Server.Rewrites.SafeSearch;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Controllers;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers which cluster-sync events <see cref="BlockedServicesController" /> and
///     <see cref="SafeSearchController" /> publish. It builds each controller by hand over EF Core
///     repositories on a temporary SQLite database and a capturing event publisher, without a web
///     host. A Blocked Services list change and a global Safe Search change must publish; a
///     targeted Blocked Services change and any client- or group-scoped Safe Search change must
///     not, because assignments and scopes exist only on this node.
/// </summary>
public sealed class BlockedServicesAndSafeSearchClusterEventTests : IDisposable
{
    private static readonly Guid NodeId = Guid.Parse("cccccccc-0000-0000-0000-000000000000");

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-web-cluster-event-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly NinlilDnsContext _context;
    private readonly DnsFilterListRepository _lists;
    private readonly DnsRewriteRepository _rewrites;
    private readonly DnsClientRepository _clients;
    private readonly DnsClientGroupRepository _groups;
    private readonly DnsClientListAssignmentRepository _clientAssignments;
    private readonly DnsClientGroupListAssignmentRepository _groupAssignments;
    private readonly DnsFilterListEntryRepository _entries;
    private readonly EntityFrameworkUnitOfWork _unitOfWork;
    private readonly CapturingPublisher _publisher = new();
    private readonly FixedOptionsMonitor<ClusterOptions> _clusterOptions = new(
        new ClusterOptions { NodeId = NodeId }
    );

    public BlockedServicesAndSafeSearchClusterEventTests()
    {
        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");

        _context = new NinlilDnsContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _lists = new DnsFilterListRepository(_context);
        _rewrites = new DnsRewriteRepository(_context);
        _clients = new DnsClientRepository(_context);
        _groups = new DnsClientGroupRepository(_context);
        _clientAssignments = new DnsClientListAssignmentRepository(_context);
        _groupAssignments = new DnsClientGroupListAssignmentRepository(_context);
        _entries = new DnsFilterListEntryRepository(_context);
        _unitOfWork = new EntityFrameworkUnitOfWork(_context);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    private BlockedServicesController CreateBlockedServices() =>
        new(
            _lists,
            _groupAssignments,
            _unitOfWork,
            _clients,
            _groups,
            _clientAssignments,
            _entries,
            _publisher,
            _clusterOptions
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

    private SafeSearchController CreateSafeSearch() =>
        new(
            _rewrites,
            new FixedOptionsMonitor<DnsRewriteOptions>(new DnsRewriteOptions { Enable = true }),
            new RuntimeConfigurationOptions
            {
                ConfigurationFilePath = Path.Combine(
                    Path.GetTempPath(),
                    $"ninlil-web-cluster-event-{Guid.NewGuid():N}.json"
                ),
            },
            _unitOfWork,
            _clients,
            _groups,
            _publisher,
            _clusterOptions,
            new ClusterConfigSyncSettingsRecorder(
                new SettingsClusterSyncRoundTripTests.FakeStateRepository(),
                new SettingsClusterSyncRoundTripTests.FakeUnitOfWork(),
                _clusterOptions,
                _publisher
            )
        );

    private async Task<Guid> AddClientAsync()
    {
        DnsClientEntity client = new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "c",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await _clients.AddAsync(client);
        await _unitOfWork.SaveChangesAsync();
        return client.Id;
    }

    [Fact]
    public async Task BlockedServicesEnable_ForANewList_PublishesOneEventAndStampsTheNode()
    {
        IActionResult result = (
            await CreateBlockedServices().Enable("tiktok", null, null, default)
        ).Result;

        Assert.IsType<OkObjectResult>(result);
        DnsFilterListEntity list = Assert.Single(await _lists.ListAsync());
        Assert.Equal(NodeId, list.OriginatingNodeId);
        DnsFilterListChangedEvent published = Assert.IsType<DnsFilterListChangedEvent>(
            Assert.Single(_publisher.Events)
        );
        Assert.Equal(list.Id, published.ListId);
        Assert.False(published.IsDeleted);
    }

    [Fact]
    public async Task BlockedServicesEnable_ForAnEnabledListAndAClient_PublishesNothingMore()
    {
        Guid clientId = await AddClientAsync();
        BlockedServicesController controller = CreateBlockedServices();
        await controller.Enable("tiktok", null, null, default);
        _publisher.Events.Clear();

        await controller.Enable("tiktok", clientId, null, default);

        Assert.Empty(_publisher.Events);
    }

    [Fact]
    public async Task BlockedServicesEnable_ForADisabledList_PublishesOneEvent()
    {
        BlockedServicesController controller = CreateBlockedServices();
        await controller.Enable("tiktok", null, null, default);
        DnsFilterListEntity list = Assert.Single(await _lists.ListAsync());
        list.IsDisabled = true;
        list.OriginatingNodeId = null;
        _lists.Update(list);
        await _unitOfWork.SaveChangesAsync();
        _publisher.Events.Clear();

        await controller.Enable("tiktok", null, null, default);

        Assert.IsType<DnsFilterListChangedEvent>(Assert.Single(_publisher.Events));
        Assert.Equal(NodeId, (await _lists.GetByIdAsync(list.Id)).OriginatingNodeId);
    }

    [Fact]
    public async Task BlockedServicesDisable_WithoutATarget_PublishesOneDeleteEvent()
    {
        BlockedServicesController controller = CreateBlockedServices();
        await controller.Enable("tiktok", null, null, default);
        Guid listId = Assert.Single(await _lists.ListAsync()).Id;
        _publisher.Events.Clear();

        await controller.Disable("tiktok", null, null, default);

        DnsFilterListChangedEvent published = Assert.IsType<DnsFilterListChangedEvent>(
            Assert.Single(_publisher.Events)
        );
        Assert.Equal(listId, published.ListId);
        Assert.True(published.IsDeleted);
        Assert.Empty(await _lists.ListAsync());
    }

    [Fact]
    public async Task BlockedServicesDisable_ForAClient_PublishesNothing()
    {
        Guid clientId = await AddClientAsync();
        BlockedServicesController controller = CreateBlockedServices();
        await controller.Enable("tiktok", clientId, null, default);
        _publisher.Events.Clear();

        await controller.Disable("tiktok", clientId, null, default);

        Assert.Empty(_publisher.Events);
        Assert.Single(await _lists.ListAsync());
    }

    [Fact]
    public async Task SafeSearchEnable_Globally_PublishesOneEventPerRewriteWithTheNodeStamped()
    {
        SafeSearchCatalogEntry entry = SafeSearchCatalog.Entries[0];

        await CreateSafeSearch().Enable(entry.Key, null, null, default);

        IReadOnlyList<DnsRewriteEntity> rewrites = await _rewrites.ListAsync();
        Assert.Equal(entry.Mappings.Count, rewrites.Count);
        Assert.All(rewrites, rewrite => Assert.Equal(NodeId, rewrite.OriginatingNodeId));
        Assert.Equal(
            rewrites.Select(rewrite => rewrite.Id).Order(),
            _publisher
                .Events.Cast<DnsRewriteChangedEvent>()
                .Select(published => published.RewriteId)
                .Order()
        );
        Assert.All(
            _publisher.Events.Cast<DnsRewriteChangedEvent>(),
            published => Assert.False(published.IsDeleted)
        );
    }

    [Fact]
    public async Task SafeSearchDisable_Globally_PublishesDeleteEventsForTheRemovedRewrites()
    {
        SafeSearchCatalogEntry entry = SafeSearchCatalog.Entries[0];
        SafeSearchController controller = CreateSafeSearch();
        await controller.Enable(entry.Key, null, null, default);
        List<Guid> ids = [.. (await _rewrites.ListAsync()).Select(rewrite => rewrite.Id)];
        _publisher.Events.Clear();

        await controller.Disable(entry.Key, null, null, default);

        Assert.Equal(
            ids.Order(),
            _publisher
                .Events.Cast<DnsRewriteChangedEvent>()
                .Select(published => published.RewriteId)
                .Order()
        );
        Assert.All(
            _publisher.Events.Cast<DnsRewriteChangedEvent>(),
            published => Assert.True(published.IsDeleted)
        );
        Assert.Empty(await _rewrites.ListAsync());
    }

    [Fact]
    public async Task SafeSearchEnableAndDisable_ForAClient_PublishNothingAndStampNoNode()
    {
        Guid clientId = await AddClientAsync();
        SafeSearchCatalogEntry entry = SafeSearchCatalog.Entries[0];
        SafeSearchController controller = CreateSafeSearch();

        await controller.Enable(entry.Key, clientId, null, default);

        IReadOnlyList<DnsRewriteEntity> rewrites = await _rewrites.ListAsync();
        Assert.NotEmpty(rewrites);
        Assert.All(rewrites, rewrite => Assert.Null(rewrite.OriginatingNodeId));
        Assert.Empty(_publisher.Events);

        await controller.Disable(entry.Key, clientId, null, default);

        Assert.Empty(_publisher.Events);
        Assert.Empty(await _rewrites.ListAsync());
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
