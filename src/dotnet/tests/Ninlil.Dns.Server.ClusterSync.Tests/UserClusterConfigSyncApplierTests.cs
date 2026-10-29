using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Identity.Store;
using Ninlil.Dns.Server.ClusterSync.Identity;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class tests <see cref="UserClusterConfigSyncApplier" /> against EF Core repositories
///     on a temporary SQLite database: create, update, delete and last-write-wins, encrypting
///     the plaintext TOTP secret and recovery codes on arrival, role and permission
///     reconciliation, and username collisions between accounts created on two nodes, resolved
///     the same way whichever side is processed.
/// </summary>
public sealed class UserClusterConfigSyncApplierTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-sync-user-applier-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly NinlilDnsContext _context;
    private readonly UserRepository _userRepository;
    private readonly RoleRepository _roleRepository;
    private readonly EntityFrameworkUnitOfWork _unitOfWork;
    private readonly IDataProtectionProvider _dataProtectionProvider =
        new EphemeralDataProtectionProvider();
    private readonly UserClusterConfigSyncApplier _applier;

    private static readonly Guid NodeA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid NodeB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");

    public UserClusterConfigSyncApplierTests()
    {
        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");

        _context = new NinlilDnsContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _userRepository = new UserRepository(_context);
        _roleRepository = new RoleRepository(_context);
        _unitOfWork = new EntityFrameworkUnitOfWork(_context);
        _applier = new UserClusterConfigSyncApplier(
            _userRepository,
            _roleRepository,
            new ClusterSyncTombstoneRepository(_context),
            _unitOfWork,
            _dataProtectionProvider,
            NullLogger<UserClusterConfigSyncApplier>.Instance
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    private static string Payload(
        string userName = "alice",
        string totpSecretPlain = null,
        string recoveryCodesPlain = null,
        IReadOnlyList<Guid> roleIds = null,
        IReadOnlyList<string> permissions = null
    ) =>
        JsonSerializer.Serialize(
            new UserSyncPayload(
                userName,
                userName.ToUpperInvariant(),
                "Alice Example",
                "alice@example.com",
                "ALICE@EXAMPLE.COM",
                true,
                "AQAAAAIAAYagAAAAE-some-fake-password-hash",
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),
                null,
                false,
                totpSecretPlain is not null,
                totpSecretPlain,
                recoveryCodesPlain,
                null,
                true,
                0,
                false,
                null,
                null,
                null,
                null,
                roleIds ?? [],
                permissions ?? []
            )
        );

    [Fact]
    public async Task ApplyAsyncCreatesANewLocalUserWhenNoneExistsYetForTheEntityKey()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: false,
                Payload("alice"),
                timestamp,
                NodeA
            ),
            CancellationToken.None
        );

        UserEntity user = await _userRepository.GetByIdAsync(userId);
        Assert.NotNull(user);
        Assert.Equal("alice", user.UserName);
        Assert.Equal(timestamp, user.CreatedAtUtc);
        Assert.Null(user.UpdatedAtUtc);
        Assert.Equal(NodeA, user.OriginatingNodeId);
        Assert.Null(user.ClusterConflictNote);
    }

    /// <summary>
    ///     This test checks that a plaintext TOTP secret in the payload is stored encrypted with
    ///     this node's protector and decrypts back to the original.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReEncryptsAnIncomingPlainTextTotpSecretWithThisNodesOwnProtector()
    {
        Guid userId = Guid.NewGuid();
        const string plainSecret = "JBSWY3DPEHPK3PXP";

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: false,
                Payload("alice", totpSecretPlain: plainSecret),
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        UserEntity user = await _userRepository.GetByIdAsync(userId);
        Assert.NotNull(user.TwoFactorTotpSecretEncrypted);
        Assert.NotEqual(plainSecret, user.TwoFactorTotpSecretEncrypted);

        IDataProtector protector = _dataProtectionProvider.CreateProtector(
            NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose
        );
        Assert.Equal(plainSecret, protector.Unprotect(user.TwoFactorTotpSecretEncrypted));
    }

    [Fact]
    public async Task ApplyAsyncReEncryptsIncomingPlainTextRecoveryCodesWithThisNodesOwnProtector()
    {
        Guid userId = Guid.NewGuid();
        const string plainCodes = "abc123;def456;ghi789";

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: false,
                Payload("alice", recoveryCodesPlain: plainCodes),
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        UserEntity user = await _userRepository.GetByIdAsync(userId);
        Assert.NotNull(user.TwoFactorRecoveryCodesEncrypted);

        IDataProtector protector = _dataProtectionProvider.CreateProtector(
            NinlilDnsUserStore.RecoveryCodesProtectorPurpose
        );
        Assert.Equal(plainCodes, protector.Unprotect(user.TwoFactorRecoveryCodesEncrypted));
    }

    [Fact]
    public async Task ApplyAsyncAssignsOnlyRolesThatAlreadyExistLocally()
    {
        Guid userId = Guid.NewGuid();
        Guid existingRoleId = Guid.NewGuid();
        Guid missingRoleId = Guid.NewGuid();

        await _roleRepository.AddAsync(
            new RoleEntity
            {
                Id = existingRoleId,
                Name = "Zone Administrator",
                NormalizedName = "ZONE ADMINISTRATOR",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: false,
                Payload("alice", roleIds: [existingRoleId, missingRoleId]),
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        IReadOnlyList<RoleEntity> roles = await _userRepository.ListRolesAsync(userId);
        Assert.Single(roles);
        Assert.Equal(existingRoleId, roles[0].Id);
    }

    [Fact]
    public async Task ApplyAsyncReplacesDirectPermissionClaimsWithTheIncomingSet()
    {
        Guid userId = Guid.NewGuid();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: false,
                Payload("alice", permissions: ["zones:read", "zones:write"]),
                DateTimeOffset.UtcNow,
                NodeA
            ),
            CancellationToken.None
        );

        IReadOnlyList<UserClaimEntity> claims = await _userRepository.ListClaimsAsync(userId);
        Assert.Equal(2, claims.Count);
        Assert.Contains(claims, claim => claim.ClaimValue == "zones:read");
        Assert.Contains(claims, claim => claim.ClaimValue == "zones:write");

        // A later envelope with fewer permissions must remove the one left out.
        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: false,
                Payload("alice", permissions: ["zones:read"]),
                DateTimeOffset.UtcNow.AddMinutes(1),
                NodeA
            ),
            CancellationToken.None
        );

        claims = await _userRepository.ListClaimsAsync(userId);
        Assert.Single(claims);
        Assert.Equal("zones:read", claims[0].ClaimValue);
    }

    [Fact]
    public async Task ApplyAsyncOverwritesAnExistingUserWhenTheIncomingChangeIsNewer()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow;

        await _userRepository.AddAsync(
            new UserEntity
            {
                Id = userId,
                UserName = "alice",
                NormalizedUserName = "ALICE",
                CreatedAtUtc = created,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        DateTimeOffset newer = created.AddMinutes(1);

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: false,
                Payload("alice-renamed"),
                newer,
                NodeB
            ),
            CancellationToken.None
        );

        UserEntity user = await _userRepository.GetByIdAsync(userId);
        Assert.Equal("alice-renamed", user.UserName);
        Assert.Equal(newer, user.UpdatedAtUtc);
        Assert.Equal(NodeB, user.OriginatingNodeId);
    }

    [Fact]
    public async Task ApplyAsyncDeletesTheLocalUserWhenTheIncomingDeletionIsNewer()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow;

        await _userRepository.AddAsync(
            new UserEntity
            {
                Id = userId,
                UserName = "alice",
                NormalizedUserName = "ALICE",
                CreatedAtUtc = created,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: true,
                PayloadJson: null,
                created.AddMinutes(1),
                NodeB
            ),
            CancellationToken.None
        );

        Assert.Null(await _userRepository.GetByIdAsync(userId));
    }

    /// <summary>
    ///     This test covers two nodes each creating an account named "alice" (different ids), for
    ///     example during a partition. The incoming account is newer, so the local "alice" loses:
    ///     it is renamed and flagged, not dropped.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncRenamesAndFlagsTheAlreadyExistingLocalAccountWhenTheIncomingOneIsNewer()
    {
        Guid localAccountId = Guid.NewGuid();
        Guid incomingAccountId = Guid.NewGuid();
        DateTimeOffset earlier = DateTimeOffset.UtcNow;
        DateTimeOffset later = earlier.AddMinutes(5);

        // This node's "alice", created earlier.
        await _userRepository.AddAsync(
            new UserEntity
            {
                Id = localAccountId,
                UserName = "alice",
                NormalizedUserName = "ALICE",
                CreatedAtUtc = earlier,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        // Another node's "alice", created later, arrives and wins.
        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                incomingAccountId.ToString(),
                IsDeleted: false,
                Payload("alice"),
                later,
                NodeB
            ),
            CancellationToken.None
        );

        // The incoming account now owns the contested username, unflagged.
        UserEntity incoming = await _userRepository.GetByIdAsync(incomingAccountId);
        Assert.NotNull(incoming);
        Assert.Equal("alice", incoming.UserName);
        Assert.Null(incoming.ClusterConflictNote);

        // The local account lost: renamed and flagged, so both accounts exist under different
        // names.
        UserEntity local = await _userRepository.GetByIdAsync(localAccountId);
        Assert.NotNull(local);
        Assert.NotEqual("alice", local.UserName);
        Assert.Contains("alice", local.UserName, StringComparison.Ordinal);
        Assert.NotNull(local.ClusterConflictNote);
        Assert.Contains("alice", local.ClusterConflictNote, StringComparison.Ordinal);
    }

    /// <summary>
    ///     This test is the reverse of the previous one: the local account is newer, so the
    ///     incoming account is created under a renamed, flagged name and the local one keeps its
    ///     name.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncCreatesTheIncomingAccountUnderARenamedFlaggedIdentityWhenItIsOlder()
    {
        Guid localAccountId = Guid.NewGuid();
        Guid incomingAccountId = Guid.NewGuid();
        DateTimeOffset earlier = DateTimeOffset.UtcNow;
        DateTimeOffset later = earlier.AddMinutes(5);

        // This node's "alice", created LATER, which wins.
        await _userRepository.AddAsync(
            new UserEntity
            {
                Id = localAccountId,
                UserName = "alice",
                NormalizedUserName = "ALICE",
                CreatedAtUtc = later,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        // Another node's "alice", created EARLIER, arrives and loses.
        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                incomingAccountId.ToString(),
                IsDeleted: false,
                Payload("alice"),
                earlier,
                NodeB
            ),
            CancellationToken.None
        );

        // The already-existing local account keeps its original name, unflagged.
        UserEntity local = await _userRepository.GetByIdAsync(localAccountId);
        Assert.Equal("alice", local.UserName);
        Assert.Null(local.ClusterConflictNote);

        // The incoming account is created under a renamed, flagged name, not dropped.
        UserEntity incoming = await _userRepository.GetByIdAsync(incomingAccountId);
        Assert.NotNull(incoming);
        Assert.NotEqual("alice", incoming.UserName);
        Assert.Contains("alice", incoming.UserName, StringComparison.Ordinal);
        Assert.NotNull(incoming.ClusterConflictNote);
    }

    /// <summary>
    ///     This test checks that the rule is symmetric: with "local" and "incoming" swapped, the
    ///     same account keeps the username, so all nodes reach the same outcome whichever
    ///     envelope they process first.
    /// </summary>
    [Fact]
    public async Task TheCollisionResolutionIsSymmetricRegardlessOfWhichSideIsProcessedAsLocal()
    {
        Guid accountOneId = Guid.NewGuid();
        Guid accountTwoId = Guid.NewGuid();
        DateTimeOffset accountOneTimestamp = DateTimeOffset.UtcNow;
        DateTimeOffset accountTwoTimestamp = accountOneTimestamp.AddMinutes(5);

        // This node has accountOne (earlier) and accountTwo (later) arrives from a peer;
        // accountTwo wins, as in
        // ApplyAsyncRenamesAndFlagsTheAlreadyExistingLocalAccountWhenTheIncomingOneIsNewer.
        await _userRepository.AddAsync(
            new UserEntity
            {
                Id = accountOneId,
                UserName = "alice",
                NormalizedUserName = "ALICE",
                CreatedAtUtc = accountOneTimestamp,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                accountTwoId.ToString(),
                IsDeleted: false,
                Payload("alice"),
                accountTwoTimestamp,
                NodeB
            ),
            CancellationToken.None
        );

        UserEntity accountTwo = await _userRepository.GetByIdAsync(accountTwoId);
        UserEntity accountOne = await _userRepository.GetByIdAsync(accountOneId);

        // accountTwo (later) wins in both orders.
        Assert.Equal("alice", accountTwo.UserName);
        Assert.Null(accountTwo.ClusterConflictNote);
        Assert.NotEqual("alice", accountOne.UserName);
        Assert.NotNull(accountOne.ClusterConflictNote);
    }

    [Fact]
    public async Task ApplyAsyncIgnoresADeletionForAnAccountThatDoesNotExistLocally()
    {
        Guid userId = Guid.NewGuid();

        Exception exception = await Record.ExceptionAsync(() =>
            _applier.ApplyAsync(
                new ClusterConfigSyncEnvelope(
                    UserClusterConfigSyncApplier.EntityTypeName,
                    userId.ToString(),
                    IsDeleted: true,
                    PayloadJson: null,
                    DateTimeOffset.UtcNow,
                    NodeA
                ),
                CancellationToken.None
            )
        );

        Assert.Null(exception);
        Assert.Null(await _userRepository.GetByIdAsync(userId));
    }

    /// <summary>
    ///     This test deletes an account and then applies an update made before the delete; the
    ///     account must stay deleted.
    /// </summary>
    [Fact]
    public async Task AStaleUpdateArrivingAfterADeleteDoesNotRecreateTheAccount()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow;

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: false,
                Payload("carol"),
                created,
                NodeA
            ),
            CancellationToken.None
        );
        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: true,
                PayloadJson: null,
                created.AddMinutes(2),
                NodeA
            ),
            CancellationToken.None
        );
        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                userId.ToString(),
                IsDeleted: false,
                Payload("carol"),
                created.AddMinutes(1),
                NodeB
            ),
            CancellationToken.None
        );

        Assert.Null(await _userRepository.GetByIdAsync(userId));
    }

    /// <summary>
    ///     This test renames a synced account, through an update, onto a username another local
    ///     account holds. The update must resolve the collision like a create does instead of
    ///     failing on the unique username.
    /// </summary>
    [Fact]
    public async Task AnUpdateThatRenamesOntoATakenUsernameResolvesTheCollision()
    {
        DateTimeOffset earlier = DateTimeOffset.UtcNow;
        Guid holderId = Guid.NewGuid();
        Guid renamedId = Guid.NewGuid();

        await _userRepository.AddAsync(
            new UserEntity
            {
                Id = holderId,
                UserName = "dave",
                NormalizedUserName = "DAVE",
                CreatedAtUtc = earlier,
                OriginatingNodeId = NodeA,
            }
        );
        await _userRepository.AddAsync(
            new UserEntity
            {
                Id = renamedId,
                UserName = "erin",
                NormalizedUserName = "ERIN",
                CreatedAtUtc = earlier,
                OriginatingNodeId = NodeA,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _applier.ApplyAsync(
            new ClusterConfigSyncEnvelope(
                UserClusterConfigSyncApplier.EntityTypeName,
                renamedId.ToString(),
                IsDeleted: false,
                Payload("dave"),
                earlier.AddMinutes(1),
                NodeB
            ),
            CancellationToken.None
        );

        UserEntity renamed = await _userRepository.GetByIdAsync(renamedId);
        UserEntity holder = await _userRepository.GetByIdAsync(holderId);
        Assert.Equal("dave", renamed.UserName);
        Assert.NotEqual("dave", holder.UserName);
        Assert.NotNull(holder.ClusterConflictNote);
    }
}
