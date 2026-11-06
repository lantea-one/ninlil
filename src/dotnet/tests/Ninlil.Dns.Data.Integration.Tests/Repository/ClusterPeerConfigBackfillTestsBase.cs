using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Maintenance;
using Ninlil.Dns.Server.Cluster.Backfill;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of <see cref="IClusterPeerConfigBackfillRepository" /> and of
///     <see cref="IRepository{TEntity}.ListIdsAsync" /> that run against every engine, one
///     subclass per engine: the id snapshot with and without a predicate, the unique index that
///     allows one backfill request per peer (a second row throws the
///     <see cref="ConflictException" /> the callers see on every engine), the list of pending
///     peers, and the backfill runner's orphan rule (a request whose peer row is gone is deleted
///     and nothing is sent; only MongoDB can hold such a request, because the relational engines
///     cascade the peer delete). The cascade on peer delete is relational only and is in
///     <see cref="ClusterPeerConfigBackfillCascadeTestsBase" />. Each test uses a fresh database.
/// </summary>
public abstract class ClusterPeerConfigBackfillTestsBase(EngineFixtureBase fixture)
{
    /// <summary>
    ///     An id snapshot with a predicate returns the ids of the matching rows and no others.
    /// </summary>
    [Fact]
    public async Task ListIdsAsync_WithAPredicate_ReturnsOnlyTheMatchingIds()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        ClusterNodeEntity keep = NewNode("keep");
        ClusterNodeEntity alsoKeep = NewNode("keep");
        ClusterNodeEntity other = NewNode("other");
        await SaveNodesAsync(database, keep, alsoKeep, other);

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IClusterNodeRepository repository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();

        IReadOnlyList<Guid> filtered = await repository.ListIdsAsync(node => node.Name == "keep");
        IReadOnlyList<Guid> all = await repository.ListIdsAsync();

        Assert.Equal(new[] { keep.Id, alsoKeep.Id }.Order(), filtered.Order());
        Assert.Equal(new[] { keep.Id, alsoKeep.Id, other.Id }.Order(), all.Order());
    }

    /// <summary>
    ///     An id snapshot of an empty table is an empty list, not null.
    /// </summary>
    [Fact]
    public async Task ListIdsAsync_OnAnEmptyTable_ReturnsNoIds()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IReadOnlyList<Guid> ids = await scope
            .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
            .ListIdsAsync();

        Assert.NotNull(ids);
        Assert.Empty(ids);
    }

    /// <summary>
    ///     A second request row for the same peer is rejected with a conflict, and a request for
    ///     another peer is still accepted.
    /// </summary>
    [Fact]
    public async Task ClusterPeerConfigBackfill_SecondRowForTheSameNode_IsRejected()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await EnsureIndexesAsync(database);
        ClusterNodeEntity node = NewNode("peer");
        ClusterNodeEntity otherNode = NewNode("other-peer");
        await SaveNodesAsync(database, node, otherNode);
        await SaveRequestAsync(database, node.Id);

        await Assert.ThrowsAsync<ConflictException>(() => SaveRequestAsync(database, node.Id));

        await SaveRequestAsync(database, otherNode.Id);
    }

    /// <summary>
    ///     The pending list holds the identifier of every peer that has a request, and no other.
    /// </summary>
    [Fact]
    public async Task ListPendingClusterNodeIdsAsync_ReturnsEveryRequestedPeer()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await EnsureIndexesAsync(database);
        ClusterNodeEntity first = NewNode("first");
        ClusterNodeEntity second = NewNode("second");
        ClusterNodeEntity idle = NewNode("idle");
        await SaveNodesAsync(database, first, second, idle);
        await SaveRequestAsync(database, first.Id);
        await SaveRequestAsync(database, second.Id);

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IClusterPeerConfigBackfillRepository repository =
            scope.ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>();

        IReadOnlyList<Guid> pending = await repository.ListPendingClusterNodeIdsAsync();
        ClusterPeerConfigBackfillEntity found = await repository.FindByClusterNodeIdAsync(first.Id);

        Assert.Equal(new[] { first.Id, second.Id }.Order(), pending.Order());
        Assert.Equal(first.Id, found?.ClusterNodeId);
        Assert.Null(await repository.FindByClusterNodeIdAsync(idle.Id));
    }

    /// <summary>
    ///     A conditional remove with the request time a pass read at its start keeps a request
    ///     whose time moved after that, and removes one whose time is unchanged. This is what makes
    ///     the end of a pass safe against a request that commits meanwhile.
    /// </summary>
    [Fact]
    public async Task RemoveIfUnchangedAsync_AfterTheRequestTimeMoved_KeepsTheRequest()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await EnsureIndexesAsync(database);
        ClusterNodeEntity node = NewNode("peer");
        await SaveNodesAsync(database, node);
        await SaveRequestAsync(database, node.Id);
        DateTimeOffset readAtPassStart = (await FindRequestAsync(database, node.Id)).RequestedAtUtc;

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IClusterPeerConfigBackfillRepository repository =
            scope.ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>();
        Assert.True(await repository.TouchAsync(node.Id, readAtPassStart.AddMinutes(1)));

        Assert.False(await repository.RemoveIfUnchangedAsync(node.Id, readAtPassStart));
        ClusterPeerConfigBackfillEntity kept = await FindRequestAsync(database, node.Id);
        Assert.NotNull(kept);

        Assert.True(await repository.RemoveIfUnchangedAsync(node.Id, kept.RequestedAtUtc));
        Assert.Null(await FindRequestAsync(database, node.Id));
    }

    /// <summary>
    ///     Touching and recording a failure change one existing request in place and report false
    ///     for a peer that has none.
    /// </summary>
    [Fact]
    public async Task TouchAsyncAndRecordFailureAsync_UpdateTheRequestInPlace()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await EnsureIndexesAsync(database);
        ClusterNodeEntity node = NewNode("peer");
        await SaveNodesAsync(database, node);
        await SaveRequestAsync(database, node.Id);
        ClusterPeerConfigBackfillEntity before = await FindRequestAsync(database, node.Id);

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IClusterPeerConfigBackfillRepository repository =
            scope.ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>();
        DateTimeOffset attempted = before.RequestedAtUtc.AddMinutes(2);

        Assert.True(await repository.RecordFailureAsync(node.Id, attempted, "boom"));
        Assert.True(await repository.RecordFailureAsync(node.Id, attempted, "boom again"));
        Assert.False(await repository.RecordFailureAsync(Guid.NewGuid(), attempted, "x"));
        Assert.False(await repository.TouchAsync(Guid.NewGuid(), attempted));

        ClusterPeerConfigBackfillEntity after = await FindRequestAsync(database, node.Id);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(2, after.AttemptCount);
        Assert.Equal("boom again", after.LastError);
        Assert.Equal(before.RequestedAtUtc, after.RequestedAtUtc);
        Assert.NotNull(after.LastAttemptAtUtc);
    }

    /// <summary>
    ///     A backfill pass for a request whose peer row is gone deletes the request and sends
    ///     nothing. MongoDB has no foreign key, so removing the peer through the repository leaves
    ///     the request behind, as a backup restore or an older build's delete can; the relational
    ///     engines already removed it through the cascade, and the pass then has nothing to do.
    /// </summary>
    [Fact]
    public async Task Runner_ForARequestWhosePeerRowIsGone_DeletesTheRequestAndSendsNothing()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await EnsureIndexesAsync(database);
        ClusterNodeEntity node = NewNode("vanished");
        await SaveNodesAsync(database, node);
        await SaveRequestAsync(database, node.Id);
        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IClusterNodeRepository nodes =
                scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();
            nodes.Remove(await nodes.GetByIdAsync(node.Id));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        bool isMongo = database.Services.GetService<NinlilDnsMongoContext>() != null;
        if (isMongo)
            Assert.NotNull(await FindRequestAsync(database, node.Id));

        NeverSender sender = new();
        ClusterConfigBackfillRunner runner = new(
            database.Services.GetRequiredService<IServiceScopeFactory>(),
            sender,
            new ClusterConfigBackfillRequester(
                database.Services.GetRequiredService<IServiceScopeFactory>()
            ),
            NullLogger<ClusterConfigBackfillRunner>.Instance
        );

        Assert.True(await runner.RunPassAsync(node.Id, CancellationToken.None));

        Assert.Null(await FindRequestAsync(database, node.Id));
        Assert.Equal(0, sender.Calls);
    }

    private static async Task<ClusterPeerConfigBackfillEntity> FindRequestAsync(
        EngineDatabase database,
        Guid clusterNodeId
    )
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
            .FindByClusterNodeIdAsync(clusterNodeId);
    }

    /// <summary>
    ///     This class is a peer sender that counts its calls and refuses them, for a pass that
    ///     must not send anything.
    /// </summary>
    private sealed class NeverSender : IClusterConfigPeerSender
    {
        private int _calls;

        /// <summary>This property contains how many deliveries were attempted.</summary>
        public int Calls => _calls;

        /// <inheritdoc />
        public Task<bool> SendToPeerAsync(
            ClusterNodeEntity peer,
            ClusterConfigSyncEnvelope envelope,
            CancellationToken stoppingToken
        )
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    ///     This method creates the MongoDB indexes when the database is a MongoDB one, because
    ///     the test databases skip the startup guard that normally creates the unique index.
    ///     Relational databases get theirs from the schema, so nothing happens for them.
    /// </summary>
    /// <param name="database">The database under test.</param>
    internal static async Task EnsureIndexesAsync(EngineDatabase database)
    {
        NinlilDnsMongoContext context = database.Services.GetService<NinlilDnsMongoContext>();
        if (context != null)
            await MongoIndexCatalog.EnsureAsync(context, NullLogger.Instance);
    }

    /// <summary>
    ///     This method builds a peer row with every required column set.
    /// </summary>
    /// <param name="name">The peer's display name.</param>
    internal static ClusterNodeEntity NewNode(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Name = name,
            RemoteNodeId = Guid.NewGuid(),
            Host = "peer.example",
            Port = 443,
            TsigKeyName = $"key-{Guid.NewGuid():N}",
            TlsCertificateThumbprint = new string('a', 64),
        };

    /// <summary>
    ///     This method stores peer rows in one save.
    /// </summary>
    /// <param name="database">The database under test.</param>
    /// <param name="nodes">The peers to store.</param>
    internal static async Task SaveNodesAsync(
        EngineDatabase database,
        params ClusterNodeEntity[] nodes
    )
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
            .AddRangeAsync(nodes);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method stores one backfill request for a peer.
    /// </summary>
    /// <param name="database">The database under test.</param>
    /// <param name="clusterNodeId">The peer's local row identifier.</param>
    internal static async Task SaveRequestAsync(EngineDatabase database, Guid clusterNodeId)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
            .AddAsync(
                new ClusterPeerConfigBackfillEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ClusterNodeId = clusterNodeId,
                    RequestedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }
}

/// <summary>
///     This class holds the cascade test that runs on the relational engines only: removing a
///     peer removes its backfill request through the foreign key. MongoDB has no cascade; the
///     peer delete endpoint removes the request itself there (see the Web test
///     <c>Delete_OfAPeerWithAPendingBackfill_RemovesTheRequest</c>).
/// </summary>
public abstract class ClusterPeerConfigBackfillCascadeTestsBase(EngineFixtureBase fixture)
{
    /// <summary>
    ///     Removing a peer removes its request and leaves the request of another peer.
    /// </summary>
    [Fact]
    public async Task RemovingAClusterNode_RemovesItsBackfillRequest()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        ClusterNodeEntity removed = ClusterPeerConfigBackfillTestsBase.NewNode("removed");
        ClusterNodeEntity kept = ClusterPeerConfigBackfillTestsBase.NewNode("kept");
        await ClusterPeerConfigBackfillTestsBase.SaveNodesAsync(database, removed, kept);
        await ClusterPeerConfigBackfillTestsBase.SaveRequestAsync(database, removed.Id);
        await ClusterPeerConfigBackfillTestsBase.SaveRequestAsync(database, kept.Id);

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IClusterNodeRepository nodes =
                scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();
            nodes.Remove(await nodes.GetByIdAsync(removed.Id));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IReadOnlyList<Guid> pending = await check
            .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
            .ListPendingClusterNodeIdsAsync();
        Assert.Equal([kept.Id], pending);
    }
}

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteClusterPeerConfigBackfillTests(SqliteEngineFixture fixture)
    : ClusterPeerConfigBackfillTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlClusterPeerConfigBackfillTests(PostgreSqlEngineFixture fixture)
    : ClusterPeerConfigBackfillTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlClusterPeerConfigBackfillTests(MySqlEngineFixture fixture)
    : ClusterPeerConfigBackfillTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerClusterPeerConfigBackfillTests(SqlServerEngineFixture fixture)
    : ClusterPeerConfigBackfillTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillTestsBase" /> on a MongoDB replica set (Docker).</summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbClusterPeerConfigBackfillTests(MongoDbEngineFixture fixture)
    : ClusterPeerConfigBackfillTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillTestsBase" /> on a standalone MongoDB (Docker).</summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneClusterPeerConfigBackfillTests(
    MongoDbStandaloneEngineFixture fixture
) : ClusterPeerConfigBackfillTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillCascadeTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteClusterPeerConfigBackfillCascadeTests(SqliteEngineFixture fixture)
    : ClusterPeerConfigBackfillCascadeTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillCascadeTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlClusterPeerConfigBackfillCascadeTests(PostgreSqlEngineFixture fixture)
    : ClusterPeerConfigBackfillCascadeTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillCascadeTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlClusterPeerConfigBackfillCascadeTests(MySqlEngineFixture fixture)
    : ClusterPeerConfigBackfillCascadeTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillCascadeTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerClusterPeerConfigBackfillCascadeTests(SqlServerEngineFixture fixture)
    : ClusterPeerConfigBackfillCascadeTestsBase(fixture);
