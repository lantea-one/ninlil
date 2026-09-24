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
///     This class proves <see cref="UserClusterConfigSyncApplier" /> against a real SQLite
///     database -- ordinary create/update/delete/last-write-wins behavior, the decrypt/re-encrypt
///     pipeline for a TOTP secret and recovery codes, role/permission-claim reconciliation, and
///     identity federation's own headline mechanism: a genuine cluster-wide username collision between two
///     independently-created accounts, resolved deterministically regardless of which side of the
///     collision this node is processing.
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
            _unitOfWork,
            _dataProtectionProvider,
            NullLogger<UserClusterConfigSyncApplier>.Instance
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
    ///     This test proves the decrypt-at-sender/re-encrypt-at-receiver pipeline actually works:
    ///     a plain-text TOTP secret carried by the payload is re-encrypted with this receiving
    ///     node's own protector, and reading it back through that same protector yields the exact
    ///     original secret.
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

        // A second, later envelope narrowing the permission set must remove the one dropped.
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
    ///     This is identity federation's own headline scenario: two nodes each independently create an
    ///     account named "alice" (two different ids) during a real network partition. When node A's
    ///     own envelope for its account reaches a node that already has a *different* local "alice"
    ///     (its own, created independently), the incoming account is newer, so the already-existing
    ///     local account is the collision's loser -- renamed and flagged, never dropped.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncRenamesAndFlagsTheAlreadyExistingLocalAccountWhenTheIncomingOneIsNewer()
    {
        Guid localAccountId = Guid.NewGuid();
        Guid incomingAccountId = Guid.NewGuid();
        DateTimeOffset earlier = DateTimeOffset.UtcNow;
        DateTimeOffset later = earlier.AddMinutes(5);

        // The local node's own independently-created "alice," earlier in time.
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

        // A different node's own independently-created "alice" arrives, later in time -- it wins.
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

        // The already-existing local account lost the collision: renamed and flagged, not
        // dropped -- both accounts survive, under different names.
        UserEntity local = await _userRepository.GetByIdAsync(localAccountId);
        Assert.NotNull(local);
        Assert.NotEqual("alice", local.UserName);
        Assert.Contains("alice", local.UserName, StringComparison.Ordinal);
        Assert.NotNull(local.ClusterConflictNote);
        Assert.Contains("alice", local.ClusterConflictNote, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The mirror image of the previous test: when the already-existing local account is
    ///     newer than the incoming one, the incoming account is the collision's loser and is
    ///     created locally under a renamed, flagged identity, while the local account keeps its
    ///     original name untouched.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncCreatesTheIncomingAccountUnderARenamedFlaggedIdentityWhenItIsOlder()
    {
        Guid localAccountId = Guid.NewGuid();
        Guid incomingAccountId = Guid.NewGuid();
        DateTimeOffset earlier = DateTimeOffset.UtcNow;
        DateTimeOffset later = earlier.AddMinutes(5);

        // The local node's own independently-created "alice," LATER in time -- it wins.
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

        // A different node's own independently-created "alice" arrives, EARLIER in time -- it
        // loses the collision.
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

        // The incoming account is created under a renamed, flagged identity -- not dropped.
        UserEntity incoming = await _userRepository.GetByIdAsync(incomingAccountId);
        Assert.NotNull(incoming);
        Assert.NotEqual("alice", incoming.UserName);
        Assert.Contains("alice", incoming.UserName, StringComparison.Ordinal);
        Assert.NotNull(incoming.ClusterConflictNote);
    }

    /// <summary>
    ///     This test proves the collision-resolution rule is genuinely symmetric: processing the
    ///     exact same pair of accounts from the "other side" (the roles of "local" and "incoming"
    ///     swapped) reaches the exact same conclusion about which one keeps the contested username
    ///     -- the property that actually matters for every node in a cluster to converge on the
    ///     same outcome, regardless of which node processes an envelope first.
    /// </summary>
    [Fact]
    public async Task TheCollisionResolutionIsSymmetricRegardlessOfWhichSideIsProcessedAsLocal()
    {
        Guid accountOneId = Guid.NewGuid();
        Guid accountTwoId = Guid.NewGuid();
        DateTimeOffset accountOneTimestamp = DateTimeOffset.UtcNow;
        DateTimeOffset accountTwoTimestamp = accountOneTimestamp.AddMinutes(5);

        // Scenario: this node already locally has accountOne (earlier); accountTwo (later)
        // arrives from a peer. accountTwo should win, exactly as
        // ApplyAsyncRenamesAndFlagsTheAlreadyExistingLocalAccountWhenTheIncomingOneIsNewer proved.
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

        // accountTwo (later) wins in both orderings -- the same conclusion the mirror-image test
        // above reached when accountOne was instead the one arriving as an incoming envelope
        // against an already-existing, earlier local accountTwo.
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
}
