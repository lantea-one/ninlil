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
///     This class proves <see cref="FilterListClusterConfigSyncApplier" /> (and, through it,
///     <see cref="Shared.ClusterConfigSyncApplierBase{TEntity}" />'s own shared create/update/
///     delete/last-write-wins logic) against a real SQLite database -- not a mock repository.
///     Chosen as the one concrete applier exercised end-to-end here since every other applier
///     shares the exact same base-class logic; each applier's own remaining, genuinely
///     independent surface (its own payload shape) is covered separately.
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
    ///     This is the genuine, explicit last-write-wins conflict proof: two conflicting changes to
    ///     the very same entity, carrying different timestamps, applied out of chronological order
    ///     -- the later one must still win in the end regardless of arrival order.
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

        // The later change must still be the one on record -- arrival order must not matter.
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

        // A stale envelope, timestamped well before the local row's own last modification.
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
    ///     A local edit that happens after a remote deletion's own timestamp must win -- the
    ///     deletion is dropped, per <see cref="Shared.ClusterConfigSyncApplierBase{TEntity}" />'s
    ///     own documented "edits beat a stale delete" last-write-wins semantics.
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

        // A deletion timestamped BEFORE the local row's own last edit.
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
