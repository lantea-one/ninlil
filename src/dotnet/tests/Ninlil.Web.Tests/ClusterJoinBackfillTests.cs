using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Store;
using Ninlil.Dns.Server.Cluster.Backfill;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Configuration;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests the join backfill across two nodes without a web host. Node A (the
///     inviter) and node B (the joiner) each have their own SQLite database, runtime settings file
///     and key ring (<see cref="ClusterBackfillNode" />). A real
///     <see cref="ClusterConfigBackfillRunner" /> on A reads A's real sources and hands each
///     envelope, through a JSON round trip, to B's real appliers, the way
///     <c>ClusterChannelController.ConfigSync</c> does. The tests assert what B holds afterwards:
///     every synced item, secrets that B's own key ring opens, tombstones, last-write-wins merges
///     with data B already held, a pass that is cut short and then completed, and a change B
///     missed while it was away, which the next pass delivers.
/// </summary>
public sealed class ClusterJoinBackfillTests : IDisposable
{
    private static readonly DateTimeOffset Old = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mid = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset New = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ClusterBackfillNode _a = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")
    );
    private readonly ClusterBackfillNode _b = new(
        Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002")
    );

    /// <inheritdoc />
    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
    }

    [Fact]
    public async Task Backfill_AfterNodeBJoinsNodeA_GivesNodeBEverySyncedSettingAndEntityOfNodeA()
    {
        Seeded seeded = await SeedNodeAAsync();
        ClusterBackfillNode.LoopbackSender sender = new(_b);

        Assert.True(await RunPassAsync(sender));

        // Every item A sends is on B, in the same wire form (secrets as plaintext).
        List<ClusterConfigSyncEnvelope> onA = await _a.ReadAllEnvelopesAsync();
        List<ClusterConfigSyncEnvelope> onB = await _b.ReadAllEnvelopesAsync();
        Assert.Equal(
            onA.Select(e => (e.EntityType, e.EntityKey, e.IsDeleted)),
            onB.Select(e => (e.EntityType, e.EntityKey, e.IsDeleted))
        );
        Assert.Contains(
            onB,
            e => e.EntityType == "Role" && e.EntityKey == seeded.Role.Id.ToString()
        );
        Assert.Contains(
            onB,
            e => e.EntityType == "User" && e.EntityKey == seeded.User.Id.ToString()
        );
        Assert.Equal(
            5,
            onB.Count(e => e.EntityType == SettingsClusterConfigSyncEntityType.EntityTypeName)
        );

        // Only global rewrites and local-list entries travel.
        Assert.Null(await _b.FindAsync<DnsRewriteEntity>(seeded.ScopedRewrite.Id));
        Assert.NotNull(await _b.FindAsync<DnsRewriteEntity>(seeded.GlobalRewrite.Id));
        Assert.DoesNotContain(
            await _b.ListAsync<DnsFilterListEntryEntity>(),
            entry => entry.ListId == seeded.SubscribedList.Id
        );
        Assert.Equal(
            2,
            (await _b.ListAsync<DnsFilterListEntryEntity>()).Count(entry =>
                entry.ListId == seeded.LocalList.Id
            )
        );

        // The deleted rewrite is a tombstone on B, and B has no row for it.
        Assert.Contains(
            await _b.ListAsync<ClusterSyncTombstoneEntity>(),
            tombstone =>
                tombstone.EntityType == "Rewrite"
                && tombstone.EntityKey == seeded.DeletedRewriteId.ToString()
        );

        // The user's TOTP secret is encrypted again with B's key ring.
        UserEntity user = await _b.FindAsync<UserEntity>(seeded.User.Id);
        Assert.Equal(
            "TOTPSECRET",
            _b.DataProtection.CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose)
                .Unprotect(user.TwoFactorTotpSecretEncrypted)
        );

        // Settings secrets: B's file holds them only as ciphertext that B's ring opens (its own
        // source reads them back as plaintext), and neither A's ciphertext nor any ciphertext
        // is on the wire.
        string bFile = await File.ReadAllTextAsync(_b.SettingsFilePath);
        Assert.DoesNotContain("rabbit-pw", bFile);
        Assert.DoesNotContain("s3cret", bFile);
        string bSettings = string.Concat(
            onB.Where(e => e.EntityType == SettingsClusterConfigSyncEntityType.EntityTypeName)
                .Select(e => e.PayloadJson)
        );
        Assert.Contains("rabbit-pw", bSettings);
        Assert.Contains("s3cret", bSettings);

        string wire = string.Concat(sender.Payloads);
        Assert.Contains("rabbit-pw", wire);
        Assert.DoesNotContain(seeded.UserTotpCipher, wire);
        foreach (string cipher in await ProtectedValuesOfAAsync())
            Assert.DoesNotContain(cipher, wire);

        // The request is done.
        Assert.Null(await FindRequestAsync());
    }

    [Fact]
    public async Task Backfill_ToANodeThatHoldsANewerCopy_KeepsTheNewerCopy()
    {
        Seeded seeded = await SeedNodeAAsync();
        DnsRewriteEntity newerOnB = new()
        {
            Id = seeded.GlobalRewrite.Id,
            Domain = "www.example.com",
            Target = "kept-on-b.example.net",
            CreatedAtUtc = Old,
            UpdatedAtUtc = New,
        };
        await _b.AddAsync(newerOnB);

        Assert.True(await RunPassAsync(new ClusterBackfillNode.LoopbackSender(_b)));

        DnsRewriteEntity afterwards = await _b.FindAsync<DnsRewriteEntity>(newerOnB.Id);
        Assert.Equal("kept-on-b.example.net", afterwards.Target);
        // The rest of A's data arrived all the same.
        Assert.NotNull(await _b.FindAsync<RoleEntity>(seeded.Role.Id));
    }

    [Fact]
    public async Task Backfill_RunTwice_LeavesNodeBUnchanged()
    {
        await SeedNodeAAsync();
        ClusterBackfillNode.LoopbackSender sender = new(_b);

        Assert.True(await RunPassAsync(sender));
        List<ClusterConfigSyncEnvelope> afterFirst = await _b.ReadAllEnvelopesAsync();
        Assert.NotEmpty(afterFirst);

        Assert.True(await RunPassAsync(sender));
        List<ClusterConfigSyncEnvelope> afterSecond = await _b.ReadAllEnvelopesAsync();

        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task Backfill_ToANodeWithItsOwnBootstrapAdmin_KeepsBothAccountsAndFlagsTheCollision()
    {
        DateTimeOffset bootstrapAt = Mid;
        UserEntity adminOnA = NewUser("admin", bootstrapAt.AddDays(1));
        UserEntity adminOnB = NewUser("admin", bootstrapAt);
        await _a.AddAsync(adminOnA);
        await _b.AddAsync(adminOnB);

        Assert.True(await RunPassAsync(new ClusterBackfillNode.LoopbackSender(_b)));

        List<UserEntity> users = await _b.ListAsync<UserEntity>();
        Assert.Equal(2, users.Count);
        Assert.Contains(users, user => user.Id == adminOnA.Id);
        Assert.Contains(users, user => user.Id == adminOnB.Id);
        Assert.Equal(2, users.Select(user => user.NormalizedUserName).Distinct().Count());
        Assert.Contains(users, user => !string.IsNullOrEmpty(user.ClusterConflictNote));
    }

    [Fact]
    public async Task Backfill_ToARejoiningNodeThatHoldsARowNodeADeleted_RemovesTheRow()
    {
        Guid deletedKey = Guid.NewGuid();
        await _a.AddAsync(
            new ClusterSyncTombstoneEntity
            {
                Id = Guid.NewGuid(),
                EntityType = "Rewrite",
                EntityKey = deletedKey.ToString(),
                CreatedAtUtc = New,
            }
        );
        await _b.AddAsync(
            new DnsRewriteEntity
            {
                Id = deletedKey,
                Domain = "gone.example.com",
                Target = "x.example.net",
                CreatedAtUtc = Old,
            }
        );

        Assert.True(await RunPassAsync(new ClusterBackfillNode.LoopbackSender(_b)));

        Assert.Null(await _b.FindAsync<DnsRewriteEntity>(deletedKey));
    }

    [Fact]
    public async Task Backfill_InterruptedAfterHalfTheEnvelopes_CompletesOnTheNextPass()
    {
        await SeedNodeAAsync();
        int total = (await _a.ReadAllEnvelopesAsync()).Count;
        ClusterBackfillNode.LoopbackSender sender = new(_b) { SucceedOnly = total / 2 };

        Guid peerId = await _a.AddPeerAsync(_b.NodeId);
        ClusterConfigBackfillRunner runner = _a.CreateRunner(sender);
        await runner.RequestBackfillAsync(peerId);

        Assert.False(await runner.RunPassAsync(peerId, CancellationToken.None));

        // The request survives with the attempt recorded, and B holds less than A.
        ClusterPeerConfigBackfillEntity request = await FindRequestAsync();
        Assert.NotNull(request);
        Assert.Equal(1, request.AttemptCount);
        Assert.NotEmpty(request.LastError);
        Assert.True((await _b.ReadAllEnvelopesAsync()).Count < total);

        // The next pass runs in full and finishes the job.
        sender.SucceedOnly = null;
        Assert.True(await runner.RunPassAsync(peerId, CancellationToken.None));

        Assert.Null(await FindRequestAsync());
        Assert.Equal(
            (await _a.ReadAllEnvelopesAsync()).Select(e => (e.EntityType, e.EntityKey)),
            (await _b.ReadAllEnvelopesAsync()).Select(e => (e.EntityType, e.EntityKey))
        );
    }

    [Fact]
    public async Task Backfill_AfterAPeerMissedAChange_DeliversItAtTheNextSuccessfulProbe()
    {
        // A holds a rewrite that B received while it was reachable.
        DnsRewriteEntity before = NewGlobalRewrite("before.example", Old);
        await _a.AddAsync(before);
        ClusterBackfillNode.LoopbackSender sender = new(_b);
        Guid peerId = await _a.AddPeerAsync(_b.NodeId);
        ClusterBackfillNode.LoopbackClientFactory clientFactory = new(
            _a.Services.GetRequiredService<IServiceScopeFactory>(),
            _a.Services.GetRequiredService<TsigSecretProtector>(),
            _b
        );
        ClusterConfigBackfillRequester requester = new(
            _a.Services.GetRequiredService<IServiceScopeFactory>()
        );
        ClusterConfigSyncBroadcaster broadcaster = _a.CreateBroadcaster(clientFactory, requester);
        ClusterConfigBackfillRunner runner = _a.CreateRunner(sender, requester);

        // B goes away, then A saves a change. The live delivery fails and A owes B a backfill.
        clientFactory.IsDown = true;
        DnsRewriteEntity missed = NewGlobalRewrite("missed.example", New);
        await _a.AddAsync(missed);
        ClusterConfigSyncEnvelope envelope = (await _a.ReadAllEnvelopesAsync()).Single(e =>
            e.EntityKey == missed.Id.ToString()
        );
        await broadcaster.BroadcastAsync(envelope, CancellationToken.None);

        Assert.Null(await _b.FindAsync<DnsRewriteEntity>(missed.Id));
        Assert.NotNull(await FindRequestAsync());

        // The next probe reaches B: the heartbeat marks it healthy and starts one pass, which
        // sends B everything, the missed change included.
        clientFactory.IsDown = false;
        Assert.True(await runner.RunPassAsync(peerId, CancellationToken.None));

        Assert.NotNull(await _b.FindAsync<DnsRewriteEntity>(missed.Id));
        Assert.NotNull(await _b.FindAsync<DnsRewriteEntity>(before.Id));
        Assert.Null(await FindRequestAsync());
    }

    private Guid _peerId;

    private static DnsRewriteEntity NewGlobalRewrite(string domain, DateTimeOffset at) =>
        new()
        {
            Id = Guid.NewGuid(),
            Domain = domain,
            Target = "safe.example.net",
            CreatedAtUtc = at,
            UpdatedAtUtc = at,
        };

    /// <summary>
    ///     This method records a request on A for node B and runs one pass through
    ///     <paramref name="sender" />.
    /// </summary>
    private async Task<bool> RunPassAsync(ClusterBackfillNode.LoopbackSender sender)
    {
        if (_peerId == Guid.Empty)
            _peerId = await _a.AddPeerAsync(_b.NodeId);
        ClusterConfigBackfillRunner runner = _a.CreateRunner(sender);
        Assert.True(await runner.RequestBackfillAsync(_peerId));
        return await runner.RunPassAsync(_peerId, CancellationToken.None);
    }

    private async Task<ClusterPeerConfigBackfillEntity> FindRequestAsync()
    {
        await using AsyncServiceScope scope = _a.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
            .FindByClusterNodeIdAsync(
                (
                    await scope
                        .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
                        .ListAsync()
                )[0].Id
            );
    }

    private static UserEntity NewUser(string name, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            PasswordHash = "hash",
            CreatedAtUtc = createdAt,
        };

    /// <summary>
    ///     This method returns the ciphertext of every secret in A's settings file, so a test can
    ///     assert none of it is sent.
    /// </summary>
    private async Task<List<string>> ProtectedValuesOfAAsync()
    {
        List<string> values = [];
        System.Text.Json.Nodes.JsonObject root = await RuntimeConfigurationFileReader.ReadRootAsync(
            _a.SettingsFilePath,
            CancellationToken.None
        );
        foreach (ConfigurationSecretSetting secret in ConfigurationSecretSettings.All)
        {
            string value = (
                (System.Text.Json.Nodes.JsonObject)
                    RuntimeConfigurationFileReader.FindSection(root, secret.SectionPath)
            )?[secret.EncryptedKey]?.GetValue<string>();
            if (!string.IsNullOrEmpty(value))
                values.Add(value);
        }

        Assert.NotEmpty(values);
        return values;
    }

    /// <summary>The rows <see cref="SeedNodeAAsync" /> creates that tests look up again.</summary>
    private sealed record Seeded(
        RoleEntity Role,
        UserEntity User,
        string UserTotpCipher,
        DnsFilterListEntity LocalList,
        DnsFilterListEntity SubscribedList,
        DnsRewriteEntity GlobalRewrite,
        DnsRewriteEntity ScopedRewrite,
        Guid DeletedRewriteId
    );

    /// <summary>
    ///     This method fills node A with one item of every synced kind: a role, a user with TOTP,
    ///     a local list with two entries, a subscribed list with an entry, an ACL rule, a global
    ///     and a scoped rewrite, a forwarder, a deleted rewrite (a tombstone) and every settings
    ///     section, written through the real recorder with secrets as a controller passes them.
    /// </summary>
    private async Task<Seeded> SeedNodeAAsync()
    {
        RoleEntity role = new()
        {
            Id = Guid.NewGuid(),
            Name = "Operators",
            NormalizedName = "OPERATORS",
            CreatedAtUtc = Mid,
        };
        UserEntity user = NewUser("alice", Mid);
        string totpCipher = _a
            .DataProtection.CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose)
            .Protect("TOTPSECRET");
        user.TwoFactorTotpSecretEncrypted = totpCipher;
        DnsFilterListEntity local = new()
        {
            Id = Guid.NewGuid(),
            Name = "Mine",
            Source = DnsFilterListSourceEnum.Local,
            CreatedAtUtc = Mid,
        };
        DnsFilterListEntity subscribed = new()
        {
            Id = Guid.NewGuid(),
            Name = "Theirs",
            Source = DnsFilterListSourceEnum.Subscribed,
            CreatedAtUtc = Mid,
        };
        DnsRewriteEntity global = new()
        {
            Id = Guid.NewGuid(),
            Domain = "www.example.com",
            Target = "safe.example.net",
            CreatedAtUtc = Mid,
        };
        DnsRewriteEntity scoped = new()
        {
            Id = Guid.NewGuid(),
            Domain = "kids.example.com",
            Target = "kids.example.net",
            ClientId = Guid.NewGuid(),
            CreatedAtUtc = Mid,
        };

        await _a.AddAsync(role);
        await _a.AddAsync(user);
        await using (AsyncServiceScope scope = _a.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IUserRepository>()
                .AddToRoleAsync(user.Id, role.Id);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await _a.AddAsync(local);
        await _a.AddAsync(subscribed);
        foreach (string value in new[] { "a.example.com", "b.example.com" })
            await _a.AddAsync(Entry(local.Id, value));
        await _a.AddAsync(Entry(subscribed.Id, "fetched.example.com"));
        await _a.AddAsync(
            new DnsAccessControlRuleEntity
            {
                Id = Guid.NewGuid(),
                Name = "lan",
                Cidr = "192.168.0.0/16",
                CreatedAtUtc = Mid,
            }
        );
        await _a.AddAsync(global);
        await _a.AddAsync(scoped);
        await _a.AddAsync(
            new DnsForwarderEntity
            {
                Id = Guid.NewGuid(),
                Name = "Corp",
                DefinitionString = "corp.example.com=192.0.2.53",
                CreatedAtUtc = Mid,
            }
        );
        Guid deleted = Guid.NewGuid();
        await _a.AddAsync(
            new ClusterSyncTombstoneEntity
            {
                Id = Guid.NewGuid(),
                EntityType = "Rewrite",
                EntityKey = deleted.ToString(),
                CreatedAtUtc = Mid,
            }
        );

        // Every synced settings section, through the recorder a controller uses.
        await using (AsyncServiceScope scope = _a.Services.CreateAsyncScope())
        {
            ClusterConfigSyncSettingsRecorder recorder =
                scope.ServiceProvider.GetRequiredService<ClusterConfigSyncSettingsRecorder>();
            foreach (ClusterSyncedSettingsSection section in ClusterSyncedSettingsCatalog.Sections)
            {
                PresetConfigurationSetting[] settings =
                    SettingsClusterConfigSyncSourceTests.SampleSettings(section.SectionName);
                await recorder.ApplyAndRecordAsync(
                    section.SectionName,
                    settings,
                    ConfigurationSecretSettings.ProtectForFile(settings, _a.DataProtection),
                    _a.SettingsFilePath,
                    CancellationToken.None
                );
            }
        }

        return new Seeded(role, user, totpCipher, local, subscribed, global, scoped, deleted);
    }

    private static DnsFilterListEntryEntity Entry(Guid listId, string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            ListId = listId,
            Value = value,
            Kind = DnsFilterEntryKindEnum.Domain,
            CreatedAtUtc = Mid,
        };
}
