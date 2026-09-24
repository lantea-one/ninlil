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
///     This class proves <see cref="RoleClusterConfigSyncApplier" /> against a real SQLite
///     database -- ordinary create/update/delete/last-write-wins behavior, permission-claim
///     reconciliation, and the same genuine role-name collision handling
///     <see cref="UserClusterConfigSyncApplierTests" /> already proves for a username.
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
            _unitOfWork,
            NullLogger<RoleClusterConfigSyncApplier>.Instance
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
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
    ///     The same genuine-partition role-name collision scenario
    ///     <see cref="UserClusterConfigSyncApplierTests" /> proves for a username: two nodes each
    ///     independently create a role named "Zone Administrator." The newer one wins the
    ///     contested name; the older, already-existing local role is renamed and flagged, never
    ///     dropped.
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
}
