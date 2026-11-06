using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Store;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.AccessControl;
using Ninlil.Dns.Server.ClusterSync.Filtering;
using Ninlil.Dns.Server.ClusterSync.Forwarding;
using Ninlil.Dns.Server.ClusterSync.Identity;
using Ninlil.Dns.Server.ClusterSync.Rewrites;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class tests the <see cref="IClusterConfigSyncSource" /> implementations against a
///     temporary SQLite database: what a backfill reads (which rows, which filters, plaintext
///     secrets, skipped rows, stamping, tombstones) and, for every entity type, that the envelope a
///     source builds is the very envelope the live subscriber sends for the same row. The sources
///     are resolved from the same registrations the host uses, and the live subscribers are real.
/// </summary>
public sealed class ClusterConfigSyncSourceTests : IDisposable
{
    private static readonly Guid OwnNode = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid OtherNode = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly DateTimeOffset Created = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    private readonly ClusterSyncTestHost _host = new(OwnNode);

    /// <inheritdoc />
    public void Dispose() => _host.Dispose();

    private async Task<List<ClusterConfigSyncEnvelope>> ReadAsync<TSource>()
        where TSource : IClusterConfigSyncSource
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        TSource source = scope.ServiceProvider.GetRequiredService<TSource>();

        List<ClusterConfigSyncEnvelope> envelopes = [];
        await foreach (
            ClusterConfigSyncEnvelope envelope in source.ReadBackfillAsync(CancellationToken.None)
        )
            envelopes.Add(envelope);
        return envelopes;
    }

    private static DnsFilterListEntity List(DnsFilterListSourceEnum source, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Source = source,
            CreatedAtUtc = Created,
        };

    private static DnsFilterListEntryEntity Entry(Guid listId, string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            ListId = listId,
            Value = value,
            Kind = DnsFilterEntryKindEnum.Domain,
            CreatedAtUtc = Created,
        };

    private static DnsRewriteEntity Rewrite(Guid? clientId, Guid? groupId) =>
        new()
        {
            Id = Guid.NewGuid(),
            Domain = "www.example.com",
            Target = "safe.example.net",
            ClientId = clientId,
            ClientGroupId = groupId,
            CreatedAtUtc = Created,
        };

    private static UserEntity User(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            PasswordHash = "hash",
            CreatedAtUtc = Created,
        };

    private static string Keys(IEnumerable<ClusterConfigSyncEnvelope> envelopes) =>
        string.Join(",", envelopes.Select(envelope => envelope.EntityKey).Order());

    private static string Keys(params Guid[] ids) =>
        string.Join(",", ids.Select(id => id.ToString()).Order());

    [Fact]
    public async Task ReadBackfillAsync_ForFilterLists_YieldsOneEnvelopePerList()
    {
        DnsFilterListEntity local = List(DnsFilterListSourceEnum.Local, "Mine");
        DnsFilterListEntity subscribed = List(DnsFilterListSourceEnum.Subscribed, "Theirs");
        await _host.AddAsync(local);
        await _host.AddAsync(subscribed);

        List<ClusterConfigSyncEnvelope> envelopes =
            await ReadAsync<FilterListClusterConfigSyncSource>();

        Assert.Equal(Keys(local.Id, subscribed.Id), Keys(envelopes));
        Assert.All(envelopes, envelope => Assert.Equal("FilterList", envelope.EntityType));
        Assert.All(envelopes, envelope => Assert.False(envelope.IsDeleted));
    }

    [Fact]
    public async Task ReadBackfillAsync_ForFilterListEntries_YieldsOnlyEntriesOfLocalLists()
    {
        DnsFilterListEntity local = List(DnsFilterListSourceEnum.Local, "Mine");
        DnsFilterListEntity otherLocal = List(DnsFilterListSourceEnum.Local, "Mine too");
        DnsFilterListEntity subscribed = List(DnsFilterListSourceEnum.Subscribed, "Theirs");
        await _host.AddAsync(local);
        await _host.AddAsync(otherLocal);
        await _host.AddAsync(subscribed);
        DnsFilterListEntryEntity a = Entry(local.Id, "a.example.com");
        DnsFilterListEntryEntity b = Entry(local.Id, "b.example.com");
        DnsFilterListEntryEntity c = Entry(otherLocal.Id, "c.example.com");
        await _host.AddAsync(a);
        await _host.AddAsync(b);
        await _host.AddAsync(c);
        await _host.AddAsync(Entry(subscribed.Id, "fetched.example.com"));

        List<ClusterConfigSyncEnvelope> envelopes =
            await ReadAsync<FilterListEntryClusterConfigSyncSource>();

        Assert.Equal(Keys(a.Id, b.Id, c.Id), Keys(envelopes));
    }

    [Fact]
    public async Task ReadBackfillAsync_ForRewrites_SkipsClientAndGroupScopedRewrites()
    {
        DnsRewriteEntity global = Rewrite(null, null);
        await _host.AddAsync(global);
        await _host.AddAsync(Rewrite(Guid.NewGuid(), null));
        await _host.AddAsync(Rewrite(null, Guid.NewGuid()));

        List<ClusterConfigSyncEnvelope> envelopes =
            await ReadAsync<RewriteClusterConfigSyncSource>();

        ClusterConfigSyncEnvelope envelope = Assert.Single(envelopes);
        Assert.Equal(global.Id.ToString(), envelope.EntityKey);
    }

    [Fact]
    public async Task ReadBackfillAsync_ForUsers_SendsDecryptedSecretsAndNoCiphertext()
    {
        UserEntity user = User("alice");
        string totpCipher = _host
            .DataProtection.CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose)
            .Protect("TOTPSECRET");
        string codesCipher = _host
            .DataProtection.CreateProtector(NinlilDnsUserStore.RecoveryCodesProtectorPurpose)
            .Protect("code1;code2");
        user.TwoFactorEnabled = true;
        user.TwoFactorTotpSecretEncrypted = totpCipher;
        user.TwoFactorRecoveryCodesEncrypted = codesCipher;
        await _host.AddAsync(user);

        List<ClusterConfigSyncEnvelope> envelopes = await ReadAsync<UserClusterConfigSyncSource>();

        ClusterConfigSyncEnvelope envelope = Assert.Single(envelopes);
        Assert.Contains("\"TwoFactorTotpSecretPlain\":\"TOTPSECRET\"", envelope.PayloadJson);
        Assert.Contains("\"TwoFactorRecoveryCodesPlain\":\"code1;code2\"", envelope.PayloadJson);
        Assert.DoesNotContain(totpCipher, envelope.PayloadJson);
        Assert.DoesNotContain(codesCipher, envelope.PayloadJson);
    }

    [Fact]
    public async Task ReadBackfillAsync_ForAUserWhoseSecretCannotBeDecrypted_SkipsThatUserAndContinues()
    {
        // A ciphertext made with another key ring is what a node holds after its keys changed.
        string foreignCipher = new EphemeralDataProtectionProvider()
            .CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose)
            .Protect("TOTPSECRET");
        UserEntity broken = User("broken");
        broken.TwoFactorTotpSecretEncrypted = foreignCipher;
        UserEntity fine = User("fine");
        await _host.AddAsync(broken);
        await _host.AddAsync(fine);

        List<ClusterConfigSyncEnvelope> envelopes = await ReadAsync<UserClusterConfigSyncSource>();

        ClusterConfigSyncEnvelope envelope = Assert.Single(envelopes);
        Assert.Equal(fine.Id.ToString(), envelope.EntityKey);
        (LogLevel level, string message) = Assert.Single(
            _host.Logs.Entries,
            entry => entry.Level == LogLevel.Warning
        );
        Assert.Contains(broken.Id.ToString(), message);
    }

    [Fact]
    public async Task ReadBackfillAsync_ForARowWithoutAnOriginatingNode_StampsThisNodesId()
    {
        DnsRewriteEntity written = Rewrite(null, null);
        DnsRewriteEntity applied = Rewrite(null, null);
        applied.OriginatingNodeId = OtherNode;
        await _host.AddAsync(written);
        await _host.AddAsync(applied);

        List<ClusterConfigSyncEnvelope> envelopes =
            await ReadAsync<RewriteClusterConfigSyncSource>();

        Assert.Equal(
            OwnNode,
            envelopes
                .Single(envelope => envelope.EntityKey == written.Id.ToString())
                .OriginatingNodeId
        );
        Assert.Equal(
            OtherNode,
            envelopes
                .Single(envelope => envelope.EntityKey == applied.Id.ToString())
                .OriginatingNodeId
        );
    }

    [Fact]
    public async Task ReadBackfillAsync_ForARowDeletedAfterTheSnapshot_SkipsIt()
    {
        DnsRewriteEntity[] rewrites =
        [
            Rewrite(null, null),
            Rewrite(null, null),
            Rewrite(null, null),
        ];
        foreach (DnsRewriteEntity rewrite in rewrites)
            await _host.AddAsync(rewrite);

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        RewriteClusterConfigSyncSource source =
            scope.ServiceProvider.GetRequiredService<RewriteClusterConfigSyncSource>();
        await using IAsyncEnumerator<ClusterConfigSyncEnvelope> enumerator = source
            .ReadBackfillAsync(CancellationToken.None)
            .GetAsyncEnumerator();

        // The first envelope proves the id snapshot is taken; the rows not read yet are deleted
        // behind the enumerator's back.
        Assert.True(await enumerator.MoveNextAsync());
        string first = enumerator.Current.EntityKey;
        foreach (
            DnsRewriteEntity rewrite in rewrites.Where(rewrite => rewrite.Id.ToString() != first)
        )
            await _host.RemoveAsync<DnsRewriteEntity>(rewrite.Id);

        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task ReadBackfillAsync_ForTombstones_YieldsADeletionEnvelopeWithTheTombstonesTimestamp()
    {
        Guid key = Guid.NewGuid();
        await _host.AddAsync(
            new ClusterSyncTombstoneEntity
            {
                Id = Guid.NewGuid(),
                EntityType = "Rewrite",
                EntityKey = key.ToString(),
                CreatedAtUtc = Created,
                UpdatedAtUtc = Updated,
                OriginatingNodeId = OtherNode,
            }
        );
        Guid localKey = Guid.NewGuid();
        await _host.AddAsync(
            new ClusterSyncTombstoneEntity
            {
                Id = Guid.NewGuid(),
                EntityType = "Rewrite",
                EntityKey = localKey.ToString(),
                CreatedAtUtc = Created,
            }
        );
        // A type no applier handles would be ignored by the peer.
        await _host.AddAsync(
            new ClusterSyncTombstoneEntity
            {
                Id = Guid.NewGuid(),
                EntityType = "Zone",
                EntityKey = Guid.NewGuid().ToString(),
                CreatedAtUtc = Created,
            }
        );

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        TombstoneClusterConfigSyncSource source = new(
            scope.ServiceProvider.GetRequiredService<IClusterSyncTombstoneRepository>(),
            [new StubApplier("Rewrite"), new StubApplier("Role")],
            scope.ServiceProvider.GetRequiredService<IOptionsMonitor<ClusterOptions>>()
        );
        List<ClusterConfigSyncEnvelope> envelopes = [];
        await foreach (
            ClusterConfigSyncEnvelope envelope in source.ReadBackfillAsync(CancellationToken.None)
        )
            envelopes.Add(envelope);

        Assert.Equal(2, envelopes.Count);
        Assert.All(envelopes, envelope => Assert.True(envelope.IsDeleted));
        Assert.All(envelopes, envelope => Assert.Null(envelope.PayloadJson));
        Assert.All(envelopes, envelope => Assert.Equal("Rewrite", envelope.EntityType));
        ClusterConfigSyncEnvelope updated = envelopes.Single(envelope =>
            envelope.EntityKey == key.ToString()
        );
        Assert.Equal(Updated, updated.LastModifiedUtc);
        Assert.Equal(OtherNode, updated.OriginatingNodeId);
        ClusterConfigSyncEnvelope created = envelopes.Single(envelope =>
            envelope.EntityKey == localKey.ToString()
        );
        Assert.Equal(Created, created.LastModifiedUtc);
        Assert.Equal(OwnNode, created.OriginatingNodeId);
        Assert.Equal(ClusterConfigBackfillStages.Tombstones, source.BackfillStage);
    }

    [Fact]
    public async Task BuildEnvelope_ForEachEntityType_MatchesTheLiveSubscribersEnvelope()
    {
        DnsFilterListEntity list = List(DnsFilterListSourceEnum.Local, "Mine");
        list.UpdatedAtUtc = Updated;
        DnsFilterListEntryEntity entry = Entry(list.Id, "a.example.com");
        DnsAccessControlRuleEntity rule = new()
        {
            Id = Guid.NewGuid(),
            Name = "Lan",
            Cidr = "203.0.113.0/24",
            Capabilities = DnsAccessControlCapabilityEnum.RecursionAccess,
            CreatedAtUtc = Created,
            OriginatingNodeId = OtherNode,
        };
        DnsRewriteEntity rewrite = Rewrite(null, null);
        DnsForwarderEntity forwarder = new()
        {
            Id = Guid.NewGuid(),
            Name = "Corp",
            DefinitionString = "corp.example.com=192.0.2.53",
            CreatedAtUtc = Created,
            UpdatedAtUtc = Updated,
        };
        RoleEntity role = new()
        {
            Id = Guid.NewGuid(),
            Name = "Operators",
            NormalizedName = "OPERATORS",
            CreatedAtUtc = Created,
        };
        UserEntity user = User("alice");
        user.TwoFactorTotpSecretEncrypted = _host
            .DataProtection.CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose)
            .Protect("TOTPSECRET");
        user.UpdatedAtUtc = Updated;
        await _host.AddAsync(list);
        await _host.AddAsync(entry);
        await _host.AddAsync(rule);
        await _host.AddAsync(rewrite);
        await _host.AddAsync(forwarder);
        await _host.AddAsync(role);
        await _host.AddAsync(user);
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IRoleRepository>()
                .AddClaimAsync(role.Id, NinlilDnsClaimTypes.Permission, "dns.zones.read");
            IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            await users.AddToRoleAsync(user.Id, role.Id);
            await users.AddClaimsAsync(user.Id, [(NinlilDnsClaimTypes.Permission, "dns.read")]);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await AssertSameAsLiveAsync<
            FilterListClusterConfigSyncSource,
            FilterListClusterSyncEventSubscriber,
            DnsFilterListChangedEvent
        >(list.Id, new DnsFilterListChangedEvent(Updated, list.Id));
        await AssertSameAsLiveAsync<
            FilterListEntryClusterConfigSyncSource,
            FilterListEntryClusterSyncEventSubscriber,
            DnsFilterListEntryChangedEvent
        >(entry.Id, new DnsFilterListEntryChangedEvent(Updated, list.Id, entry.Id));
        await AssertSameAsLiveAsync<
            AccessControlRuleClusterConfigSyncSource,
            AccessControlRuleClusterSyncEventSubscriber,
            DnsAccessControlRuleChangedEvent
        >(rule.Id, new DnsAccessControlRuleChangedEvent(Updated, rule.Id));
        await AssertSameAsLiveAsync<
            RewriteClusterConfigSyncSource,
            RewriteClusterSyncEventSubscriber,
            DnsRewriteChangedEvent
        >(rewrite.Id, new DnsRewriteChangedEvent(Updated, rewrite.Id));
        await AssertSameAsLiveAsync<
            ForwarderClusterConfigSyncSource,
            ForwarderClusterSyncEventSubscriber,
            DnsForwarderChangedEvent
        >(forwarder.Id, new DnsForwarderChangedEvent(Updated, forwarder.Id));
        await AssertSameAsLiveAsync<
            RoleClusterConfigSyncSource,
            RoleClusterSyncEventSubscriber,
            RoleChangedEvent
        >(role.Id, new RoleChangedEvent(Updated, role.Id));
        await AssertSameAsLiveAsync<
            UserClusterConfigSyncSource,
            UserClusterSyncEventSubscriber,
            UserAccountChangedEvent
        >(user.Id, new UserAccountChangedEvent(Updated, user.Id));
    }

    /// <summary>
    ///     This method sends the row's change through its live subscriber, builds the envelope
    ///     through the source's backfill read and through <c>BuildEnvelopeAsync</c>, and asserts all
    ///     three are the same value (the record compares every field, the payload string included).
    /// </summary>
    private async Task AssertSameAsLiveAsync<TSource, TSubscriber, TEvent>(
        Guid id,
        TEvent domainEvent
    )
        where TSource : IClusterConfigSyncSource
        where TSubscriber : IDnsServerEventSubscriber<TEvent>
        where TEvent : IDnsServerEvent
    {
        ClusterConfigSyncEnvelope live = await _host.SendAsync<TSubscriber, TEvent>(domainEvent);

        List<ClusterConfigSyncEnvelope> backfill = await ReadAsync<TSource>();
        ClusterConfigSyncEnvelope read = backfill.Single(envelope =>
            envelope.EntityKey == id.ToString()
        );

        Assert.Equal(live, read);
        Assert.False(live.IsDeleted);
        Assert.NotNull(live.PayloadJson);
    }

    /// <summary>
    ///     This class is an applier that only names an entity type, to tell the tombstone source
    ///     which deletions a peer can apply.
    /// </summary>
    private sealed class StubApplier(string entityType) : IClusterConfigSyncApplier
    {
        public string EntityType => entityType;

        public Task ApplyAsync(
            ClusterConfigSyncEnvelope envelope,
            CancellationToken stoppingToken
        ) => Task.CompletedTask;
    }
}
