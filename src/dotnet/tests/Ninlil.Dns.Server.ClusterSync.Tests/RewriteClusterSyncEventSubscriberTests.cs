using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Rewrites;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class tests which rewrites <see cref="RewriteClusterSyncEventSubscriber" /> broadcasts:
///     a global rewrite is sent, a rewrite scoped to a client or client group is not, because the
///     sync payload has no scope and a peer would create a global rewrite from it. It reads
///     rewrites from a temporary SQLite database and records the envelopes in a fake broadcaster.
///     A probe subclass awaits the subscriber's normally detached broadcast task.
/// </summary>
public sealed class RewriteClusterSyncEventSubscriberTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-sync-rewrite-subscriber-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly ServiceProvider _services;
    private readonly RecordingBroadcaster _broadcaster = new();
    private readonly Probe _subscriber;

    public RewriteClusterSyncEventSubscriberTests()
    {
        ServiceCollection collection = new();
        collection.AddDbContext<NinlilDnsContext>(options =>
            options.UseNinlilDnsSqlite($"Data Source={_databasePath}")
        );
        collection.AddScoped<IDnsRewriteRepository, DnsRewriteRepository>();
        collection.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        _services = collection.BuildServiceProvider();

        using (IServiceScope scope = _services.CreateScope())
            scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();

        _subscriber = new Probe(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _broadcaster,
            new FixedClusterOptions(new ClusterOptions { NodeId = Guid.NewGuid() })
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    private async Task<Guid> AddRewriteAsync(Guid? clientId, Guid? clientGroupId)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        DnsRewriteEntity rewrite = new()
        {
            Id = Guid.NewGuid(),
            Domain = "www.example.com",
            Target = "forcesafesearch.example.net",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ClientId = clientId,
            ClientGroupId = clientGroupId,
        };
        await scope.ServiceProvider.GetRequiredService<IDnsRewriteRepository>().AddAsync(rewrite);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return rewrite.Id;
    }

    [Fact]
    public async Task BroadcastChange_ForAGlobalRewrite_SendsAnEnvelope()
    {
        Guid id = await AddRewriteAsync(null, null);

        await _subscriber.RunAsync(id);

        ClusterConfigSyncEnvelope envelope = Assert.Single(_broadcaster.Envelopes);
        Assert.Equal(id.ToString(), envelope.EntityKey);
        Assert.False(envelope.IsDeleted);
    }

    [Fact]
    public async Task BroadcastChange_ForAClientScopedRewrite_SendsNothing()
    {
        Guid id = await AddRewriteAsync(Guid.NewGuid(), null);

        await _subscriber.RunAsync(id);

        Assert.Empty(_broadcaster.Envelopes);
    }

    [Fact]
    public async Task BroadcastChange_ForAGroupScopedRewrite_SendsNothing()
    {
        Guid id = await AddRewriteAsync(null, Guid.NewGuid());

        await _subscriber.RunAsync(id);

        Assert.Empty(_broadcaster.Envelopes);
    }

    /// <summary>This class exposes the subscriber's broadcast so a test can await it.</summary>
    private sealed class Probe(
        IServiceScopeFactory scopeFactory,
        IClusterConfigSyncBroadcaster broadcaster,
        IOptionsMonitor<ClusterOptions> clusterOptions
    )
        : RewriteClusterSyncEventSubscriber(
            scopeFactory,
            broadcaster,
            clusterOptions,
            NullLogger<RewriteClusterSyncEventSubscriber>.Instance
        )
    {
        public Task RunAsync(Guid id) =>
            BroadcastChangeAsync(id, false, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    /// <summary>This class records every envelope handed to it.</summary>
    private sealed class RecordingBroadcaster : IClusterConfigSyncBroadcaster
    {
        public List<ClusterConfigSyncEnvelope> Envelopes { get; } = [];

        public Task BroadcastAsync(
            ClusterConfigSyncEnvelope envelope,
            CancellationToken stoppingToken
        )
        {
            Envelopes.Add(envelope);
            return Task.CompletedTask;
        }
    }

    /// <summary>This class is an options monitor that always returns one value.</summary>
    private sealed class FixedClusterOptions(ClusterOptions value) : IOptionsMonitor<ClusterOptions>
    {
        public ClusterOptions CurrentValue => value;

        public ClusterOptions Get(string name) => value;

        public IDisposable OnChange(Action<ClusterOptions, string> listener) => null;
    }
}
