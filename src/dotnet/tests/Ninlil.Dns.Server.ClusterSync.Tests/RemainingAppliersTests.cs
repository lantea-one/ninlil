using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.ClusterSync.AccessControl;
using Ninlil.Dns.Server.ClusterSync.Filtering;
using Ninlil.Dns.Server.ClusterSync.Forwarding;
using Ninlil.Dns.Server.ClusterSync.Rewrites;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class covers the one genuinely independent thing each of the remaining four
///     appliers (<see cref="AccessControlRuleClusterConfigSyncApplier" />/
///     <see cref="RewriteClusterConfigSyncApplier" />/<see cref="ForwarderClusterConfigSyncApplier" />/
///     <see cref="FilterListEntryClusterConfigSyncApplier" />) adds on top of the shared
///     create/update/delete/last-write-wins logic <see cref="FilterListClusterConfigSyncApplierTests" />
///     already proves in full against <see cref="FilterListClusterConfigSyncApplier" />: that
///     each one's own payload actually round-trips correctly through its own real entity and
///     repository, plus <see cref="FilterListEntryClusterConfigSyncApplier" />'s own extra
///     "owning list must already exist" gate.
/// </summary>
public sealed class RemainingAppliersTests : IDisposable
{
    private static readonly Guid NodeA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-sync-remaining-appliers-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly NinlilDnsContext _context;
    private readonly EntityFrameworkUnitOfWork _unitOfWork;

    public RemainingAppliersTests()
    {
        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");

        _context = new NinlilDnsContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();
        _unitOfWork = new EntityFrameworkUnitOfWork(_context);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task AccessControlRuleApplierCreatesARuleFromAnIncomingEnvelope()
    {
        DnsAccessControlRuleRepository repository = new(_context);
        AccessControlRuleClusterConfigSyncApplier applier = new(
            repository,
            _unitOfWork,
            NullLogger<AccessControlRuleClusterConfigSyncApplier>.Instance
        );

        Guid ruleId = Guid.NewGuid();
        string payload = JsonSerializer.Serialize(
            new AccessControlRuleSyncPayload(
                "Trusted LAN",
                "203.0.113.0/24",
                DnsAccessControlCapabilityEnum.RecursionAccess,
                false
            )
        );

        await applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                AccessControlRuleClusterConfigSyncApplier.EntityTypeName,
                ruleId.ToString(),
                IsDeleted: false,
                payload,
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        DnsAccessControlRuleEntity rule = await repository.GetByIdAsync(ruleId);
        Assert.NotNull(rule);
        Assert.Equal("Trusted LAN", rule.Name);
        Assert.Equal("203.0.113.0/24", rule.Cidr);
        Assert.Equal(DnsAccessControlCapabilityEnum.RecursionAccess, rule.Capabilities);
    }

    [Fact]
    public async Task RewriteApplierCreatesARewriteFromAnIncomingEnvelope()
    {
        DnsRewriteRepository repository = new(_context);
        RewriteClusterConfigSyncApplier applier = new(
            repository,
            _unitOfWork,
            NullLogger<RewriteClusterConfigSyncApplier>.Instance
        );

        Guid rewriteId = Guid.NewGuid();
        string payload = JsonSerializer.Serialize(
            new RewriteSyncPayload("example.test", "203.0.113.5", false, "synced")
        );

        await applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                RewriteClusterConfigSyncApplier.EntityTypeName,
                rewriteId.ToString(),
                IsDeleted: false,
                payload,
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        DnsRewriteEntity rewrite = await repository.GetByIdAsync(rewriteId);
        Assert.NotNull(rewrite);
        Assert.Equal("example.test", rewrite.Domain);
        Assert.Equal("203.0.113.5", rewrite.Target);
    }

    [Fact]
    public async Task ForwarderApplierCreatesAForwarderFromAnIncomingEnvelope()
    {
        DnsForwarderRepository repository = new(_context);
        ForwarderClusterConfigSyncApplier applier = new(
            repository,
            _unitOfWork,
            NullLogger<ForwarderClusterConfigSyncApplier>.Instance
        );

        Guid forwarderId = Guid.NewGuid();
        string payload = JsonSerializer.Serialize(
            new ForwarderSyncPayload("Cloudflare", "1.1.1.1", 0, false, null)
        );

        await applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                ForwarderClusterConfigSyncApplier.EntityTypeName,
                forwarderId.ToString(),
                IsDeleted: false,
                payload,
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        DnsForwarderEntity forwarder = await repository.GetByIdAsync(forwarderId);
        Assert.NotNull(forwarder);
        Assert.Equal("Cloudflare", forwarder.Name);
        Assert.Equal("1.1.1.1", forwarder.DefinitionString);
    }

    [Fact]
    public async Task FilterListEntryApplierSkipsAnEntryWhoseOwningListDoesNotExistLocallyYet()
    {
        DnsFilterListRepository listRepository = new(_context);
        DnsFilterListEntryRepository entryRepository = new(_context);
        FilterListEntryClusterConfigSyncApplier applier = new(
            entryRepository,
            listRepository,
            _unitOfWork,
            NullLogger<FilterListEntryClusterConfigSyncApplier>.Instance
        );

        Guid entryId = Guid.NewGuid();
        Guid orphanListId = Guid.NewGuid();
        string payload = JsonSerializer.Serialize(
            new FilterListEntrySyncPayload(
                orphanListId,
                "ads.example.test",
                DnsFilterEntryKindEnum.Domain,
                null
            )
        );

        await applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                FilterListEntryClusterConfigSyncApplier.EntityTypeName,
                entryId.ToString(),
                IsDeleted: false,
                payload,
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        // The owning list was never provisioned locally, so the entry must not have been
        // created either -- it converges only once the owning list itself arrives.
        Assert.Null(await entryRepository.GetByIdAsync(entryId));
    }

    [Fact]
    public async Task FilterListEntryApplierCreatesAnEntryOnceItsOwningListAlreadyExists()
    {
        DnsFilterListRepository listRepository = new(_context);
        DnsFilterListEntryRepository entryRepository = new(_context);
        FilterListEntryClusterConfigSyncApplier applier = new(
            entryRepository,
            listRepository,
            _unitOfWork,
            NullLogger<FilterListEntryClusterConfigSyncApplier>.Instance
        );

        Guid listId = Guid.NewGuid();
        await listRepository.AddAsync(
            new DnsFilterListEntity
            {
                Id = listId,
                Name = "Local list",
                Kind = DnsFilterListKindEnum.Domain,
                Action = DnsFilterListActionEnum.Block,
                Source = DnsFilterListSourceEnum.Local,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        Guid entryId = Guid.NewGuid();
        string payload = JsonSerializer.Serialize(
            new FilterListEntrySyncPayload(
                listId,
                "ads.example.test",
                DnsFilterEntryKindEnum.Domain,
                null
            )
        );

        await applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                FilterListEntryClusterConfigSyncApplier.EntityTypeName,
                entryId.ToString(),
                IsDeleted: false,
                payload,
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        DnsFilterListEntryEntity entry = await entryRepository.GetByIdAsync(entryId);
        Assert.NotNull(entry);
        Assert.Equal(listId, entry.ListId);
        Assert.Equal("ads.example.test", entry.Value);
    }
}
