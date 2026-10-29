using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.ClusterSync.Identity;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class tests <see cref="RoleClusterConfigSyncApplier" /> against EF Core repositories
///     on a temporary SQLite database: create, update, delete and last-write-wins, permission
///     reconciliation, and role-name collisions, as <see cref="UserClusterConfigSyncApplierTests" />
///     does for usernames.
/// </summary>
public sealed class RoleClusterConfigSyncApplierTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-sync-role-applier-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly NinlilDnsContext _context;
    private readonly RoleRepository _repository;
    private readonly EntityFrameworkUnitOfWork _unitOfWork;
    private readonly RoleClusterConfigSyncApplier _applier;

    private static readonly Guid NodeA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid NodeB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");

    public RoleClusterConfigSyncApplierTests()
    {
        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");

        _context = new NinlilDnsContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _repository = new RoleRepository(_context);
        _unitOfWork = new EntityFrameworkUnitOfWork(_context);
        _applier = new RoleClusterConfigSyncApplier(
            _repository,
            new ClusterSyncTombstoneRepository(_context),
            _unitOfWork,
            NullLogger<RoleClusterConfigSyncApplier>.Instance
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    private static string Payload(
        string name = "Zone Administrator",
        IReadOnlyList<string> permissions = null
    ) =>
        JsonSerializer.Serialize(
            new RoleSyncPayload(
                name,
                name.ToUpperInvariant(),
                "Manages DNS zones.",
                Guid.NewGuid().ToString("N"),
                permissions ?? []
            )
        );

    [Fact]
    public async Task ApplyAsyncCreatesANewLocalRoleWhenNoneExistsYetForTheEntityKey()
    {
        Guid roleId = Guid.NewGuid();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                RoleClusterConfigSyncApplier.EntityTypeName,
                roleId.ToString(),
                IsDeleted: false,
                Payload(),
                timestamp,
                NodeA
            ),
            CancellationToken.None
        );

        RoleEntity role = await _repository.GetByIdAsync(roleId);
        Assert.NotNull(role);
        Assert.Equal("Zone Administrator", role.Name);
        Assert.Equal(NodeA, role.OriginatingNodeId);
        Assert.Null(role.ClusterConflictNote);
    }

    [Fact]
    public async Task ApplyAsyncReplacesPermissionClaimsWithTheIncomingSet()
    {
        Guid roleId = Guid.NewGuid();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                RoleClusterConfigSyncApplier.EntityTypeName,
                roleId.ToString(),
                IsDeleted: false,
                Payload(permissions: ["zones:read", "zones:write"]),
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        IReadOnlyList<RoleClaimEntity> claims = await _repository.ListClaimsAsync(roleId);
        Assert.Equal(2, claims.Count);

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                RoleClusterConfigSyncApplier.EntityTypeName,
                roleId.ToString(),
                IsDeleted: false,
                Payload(permissions: ["zones:read"]),
                DateTimeOffset.UtcNow.AddMinutes(1),
                NodeA
            ),
            CancellationToken.None
        );

        claims = await _repository.ListClaimsAsync(roleId);
        Assert.Single(claims);
        Assert.Equal("zones:read", claims[0].ClaimValue);
    }

    /// <summary>
    ///     This test covers a role-name collision, as <see cref="UserClusterConfigSyncApplierTests" />
    ///     does for usernames: two nodes each create a role named "Zone Administrator". The newer
    ///     one keeps the name; the older local role is renamed and flagged, not dropped.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncRenamesAndFlagsTheAlreadyExistingLocalRoleWhenTheIncomingOneIsNewer()
    {
        Guid localRoleId = Guid.NewGuid();
        Guid incomingRoleId = Guid.NewGuid();
        DateTimeOffset earlier = DateTimeOffset.UtcNow;
        DateTimeOffset later = earlier.AddMinutes(5);

        await _repository.AddAsync(
            new RoleEntity
            {
                Id = localRoleId,
                Name = "Zone Administrator",
                NormalizedName = "ZONE ADMINISTRATOR",
                CreatedAtUtc = earlier,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                RoleClusterConfigSyncApplier.EntityTypeName,
                incomingRoleId.ToString(),
                IsDeleted: false,
                Payload(),
                later,
                NodeB
            ),
            CancellationToken.None
        );

        RoleEntity incoming = await _repository.GetByIdAsync(incomingRoleId);
        Assert.Equal("Zone Administrator", incoming.Name);
        Assert.Null(incoming.ClusterConflictNote);

        RoleEntity local = await _repository.GetByIdAsync(localRoleId);
        Assert.NotNull(local);
        Assert.NotEqual("Zone Administrator", local.Name);
        Assert.NotNull(local.ClusterConflictNote);
    }

    [Fact]
    public async Task ApplyAsyncDeletesTheLocalRoleWhenTheIncomingDeletionIsNewer()
    {
        Guid roleId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow;

        await _repository.AddAsync(
            new RoleEntity
            {
                Id = roleId,
                Name = "Temporary",
                NormalizedName = "TEMPORARY",
                CreatedAtUtc = created,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                RoleClusterConfigSyncApplier.EntityTypeName,
                roleId.ToString(),
                IsDeleted: true,
                PayloadJson: null,
                created.AddMinutes(1),
                NodeB
            ),
            CancellationToken.None
        );

        Assert.Null(await _repository.GetByIdAsync(roleId));
    }

    /// <summary>
    ///     This test renames a synced role, through an older update, onto a name another local
    ///     role holds; the incoming role loses the collision and gets a renamed, flagged name.
    /// </summary>
    [Fact]
    public async Task AnOlderUpdateThatRenamesOntoATakenNameIsRenamedItself()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid holderId = Guid.NewGuid();
        Guid renamedId = Guid.NewGuid();

        await _repository.AddAsync(
            new RoleEntity
            {
                Id = holderId,
                Name = "Operators",
                NormalizedName = "OPERATORS",
                CreatedAtUtc = now,
                OriginatingNodeId = NodeA,
            }
        );
        await _repository.AddAsync(
            new RoleEntity
            {
                Id = renamedId,
                Name = "Viewers",
                NormalizedName = "VIEWERS",
                CreatedAtUtc = now.AddMinutes(-10),
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                RoleClusterConfigSyncApplier.EntityTypeName,
                renamedId.ToString(),
                IsDeleted: false,
                Payload("Operators"),
                now.AddMinutes(-5),
                NodeB
            ),
            CancellationToken.None
        );

        RoleEntity holder = await _repository.GetByIdAsync(holderId);
        RoleEntity renamed = await _repository.GetByIdAsync(renamedId);
        Assert.Equal("Operators", holder.Name);
        Assert.StartsWith("Operators-conflict-", renamed.Name);
        Assert.NotNull(renamed.ClusterConflictNote);
    }
}
