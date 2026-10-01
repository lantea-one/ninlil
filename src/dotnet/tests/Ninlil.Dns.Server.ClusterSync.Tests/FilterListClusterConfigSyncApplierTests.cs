using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.ClusterSync.Filtering;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class tests <see cref="FilterListClusterConfigSyncApplier" />, and through it the
///     create, update, delete and last-write-wins logic of
///     <see cref="Shared.ClusterConfigSyncApplierBase{TEntity}" />, against EF Core repositories
///     on a temporary SQLite database. The other simple appliers share that base, so
///     <see cref="RemainingAppliersTests" /> only checks their payload mapping.
/// </summary>
public sealed class FilterListClusterConfigSyncApplierTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-sync-filter-list-applier-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly NinlilDnsContext _context;
    private readonly DnsFilterListRepository _repository;
    private readonly EntityFrameworkUnitOfWork _unitOfWork;
    private readonly FilterListClusterConfigSyncApplier _applier;

    private static readonly Guid NodeA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid NodeB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");

    public FilterListClusterConfigSyncApplierTests()
    {
        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");

        _context = new NinlilDnsContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _repository = new DnsFilterListRepository(_context);
        _unitOfWork = new EntityFrameworkUnitOfWork(_context);
        _applier = new FilterListClusterConfigSyncApplier(
            _repository,
            _unitOfWork,
            NullLogger<FilterListClusterConfigSyncApplier>.Instance
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static string Payload(string name = "Ads", bool isDisabled = false) =>
        JsonSerializer.Serialize(
            new FilterListSyncPayload(
                name,
                DnsFilterListKindEnum.Domain,
                DnsFilterListActionEnum.Block,
                DnsFilterListSourceEnum.Local,
                null,
                null,
                isDisabled,
                null
            )
        );

    [Fact]
    public async Task ApplyAsyncCreatesANewLocalListWhenNoneExistsYetForTheEntityKey()
    {
        Guid listId = Guid.NewGuid();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                FilterListClusterConfigSyncApplier.EntityTypeName,
                listId.ToString(),
                IsDeleted: false,
                Payload("Ads"),
                timestamp,
                NodeA
            ),
            CancellationToken.None
        );

        DnsFilterListEntity list = await _repository.GetByIdAsync(listId);
        Assert.NotNull(list);
        Assert.Equal("Ads", list.Name);
        Assert.Equal(timestamp, list.CreatedAtUtc);
        Assert.Null(list.UpdatedAtUtc);
        Assert.Equal(NodeA, list.OriginatingNodeId);
    }

    /// <summary>
    ///     This test applies two conflicting changes to one entity out of order; the later one
    ///     must end up on record whatever the arrival order.
    /// </summary>
    [Fact]
    public async Task TheLaterOfTwoConflictingChangesWinsEvenWhenTheEarlierOneArrivesSecond()
    {
        Guid listId = Guid.NewGuid();
        DateTimeOffset earlier = DateTimeOffset.UtcNow;
        DateTimeOffset later = earlier.AddMinutes(5);

        // The LATER change (from Node B, renaming the list and disabling it) arrives FIRST.
        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                FilterListClusterConfigSyncApplier.EntityTypeName,
                listId.ToString(),
                IsDeleted: false,
                Payload("Renamed-By-B", isDisabled: true),
                later,
                NodeB
            ),
            CancellationToken.None
        );

        // The EARLIER change (from Node A) arrives SECOND, out of chronological order.
        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                FilterListClusterConfigSyncApplier.EntityTypeName,
                listId.ToString(),
                IsDeleted: false,
                Payload("Original-From-A", isDisabled: false),
                earlier,
                NodeA
            ),
            CancellationToken.None
        );

        // The later change is on record; arrival order does not matter.
        DnsFilterListEntity list = await _repository.GetByIdAsync(listId);
        Assert.NotNull(list);
        Assert.Equal("Renamed-By-B", list.Name);
        Assert.True(list.IsDisabled);
        Assert.Equal(NodeB, list.OriginatingNodeId);
    }

    [Fact]
    public async Task ApplyAsyncOverwritesAnExistingListWhenTheIncomingChangeIsNewer()
    {
        Guid listId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow;

        await _repository.AddAsync(
            new DnsFilterListEntity
            {
                Id = listId,
                Name = "Original",
                Kind = DnsFilterListKindEnum.Domain,
                Action = DnsFilterListActionEnum.Block,
                Source = DnsFilterListSourceEnum.Local,
                CreatedAtUtc = created,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        DateTimeOffset newer = created.AddMinutes(1);

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                FilterListClusterConfigSyncApplier.EntityTypeName,
                listId.ToString(),
                IsDeleted: false,
                Payload("Updated"),
                newer,
                NodeB
            ),
            CancellationToken.None
        );

        DnsFilterListEntity list = await _repository.GetByIdAsync(listId);
        Assert.Equal("Updated", list.Name);
        Assert.Equal(newer, list.UpdatedAtUtc);
        Assert.Equal(NodeB, list.OriginatingNodeId);
    }

    [Fact]
    public async Task ApplyAsyncSilentlyDiscardsAStaleIncomingChange()
    {
        Guid listId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow;

        await _repository.AddAsync(
            new DnsFilterListEntity
            {
                Id = listId,
                Name = "AlreadyCurrent",
                Kind = DnsFilterListKindEnum.Domain,
                Action = DnsFilterListActionEnum.Block,
                Source = DnsFilterListSourceEnum.Local,
                CreatedAtUtc = created,
                UpdatedAtUtc = created.AddMinutes(10),
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        // A stale envelope, timestamped well before the local row's last change.
        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                FilterListClusterConfigSyncApplier.EntityTypeName,
                listId.ToString(),
                IsDeleted: false,
                Payload("ShouldNeverApply"),
                created,
                NodeB
            ),
            CancellationToken.None
        );

        DnsFilterListEntity list = await _repository.GetByIdAsync(listId);
        Assert.Equal("AlreadyCurrent", list.Name);
    }

    [Fact]
    public async Task ApplyAsyncDeletesTheLocalListWhenTheIncomingDeletionIsNewer()
    {
        Guid listId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow;

        await _repository.AddAsync(
            new DnsFilterListEntity
            {
                Id = listId,
                Name = "ToBeDeleted",
                Kind = DnsFilterListKindEnum.Domain,
                Action = DnsFilterListActionEnum.Block,
                Source = DnsFilterListSourceEnum.Local,
                CreatedAtUtc = created,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                FilterListClusterConfigSyncApplier.EntityTypeName,
                listId.ToString(),
                IsDeleted: true,
                PayloadJson: null,
                created.AddMinutes(1),
                NodeB
            ),
            CancellationToken.None
        );

        Assert.Null(await _repository.GetByIdAsync(listId));
    }

    /// <summary>
    ///     This test checks that a local edit newer than a remote deletion wins and the deletion
    ///     is ignored, as last-write-wins in <see cref="Shared.ClusterConfigSyncApplierBase{TEntity}" />
    ///     gives.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncKeepsALocalEditThatIsNewerThanAnIncomingDeletion()
    {
        Guid listId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow;

        await _repository.AddAsync(
            new DnsFilterListEntity
            {
                Id = listId,
                Name = "EditedLocallyAfterTheRemoteDeleteHappened",
                Kind = DnsFilterListKindEnum.Domain,
                Action = DnsFilterListActionEnum.Block,
                Source = DnsFilterListSourceEnum.Local,
                CreatedAtUtc = created,
                UpdatedAtUtc = created.AddMinutes(10),
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        // A deletion timestamped BEFORE the local row's last edit.
        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                FilterListClusterConfigSyncApplier.EntityTypeName,
                listId.ToString(),
                IsDeleted: true,
                PayloadJson: null,
                created.AddMinutes(1),
                NodeB
            ),
            CancellationToken.None
        );

        DnsFilterListEntity list = await _repository.GetByIdAsync(listId);
        Assert.NotNull(list);
        Assert.Equal("EditedLocallyAfterTheRemoteDeleteHappened", list.Name);
    }

    [Fact]
    public async Task ApplyAsyncIgnoresADeletionForAnEntityThatDoesNotExistLocally()
    {
        Guid listId = Guid.NewGuid();

        Exception exception = await Record.ExceptionAsync(() =>
            _applier.ApplyAsync(
                new ClusterConfigSyncEnvelope(
                    FilterListClusterConfigSyncApplier.EntityTypeName,
                    listId.ToString(),
                    IsDeleted: true,
                    PayloadJson: null,
                    DateTimeOffset.UtcNow,
                    NodeA
                ),
                CancellationToken.None
            )
        );

        Assert.Null(exception);
        Assert.Null(await _repository.GetByIdAsync(listId));
    }
}
