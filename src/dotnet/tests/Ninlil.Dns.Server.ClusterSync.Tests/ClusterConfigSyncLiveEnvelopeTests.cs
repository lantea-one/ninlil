using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Store;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.AccessControl;
using Ninlil.Dns.Server.ClusterSync.Filtering;
using Ninlil.Dns.Server.ClusterSync.Forwarding;
using Ninlil.Dns.Server.ClusterSync.Identity;
using Ninlil.Dns.Server.ClusterSync.Rewrites;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class pins the wire output of the live sending subscribers, one test per synced entity
///     type, with literal expected envelopes: the entity type and key, the exact payload JSON, the
///     timestamp and the originating node. A peer parses these bytes, so a change to the payload
///     shape, the property order, the timestamp rule or the node stamping is a protocol change and
///     must fail here. Each test drives the real subscriber through its event handler against a
///     temporary SQLite database and records what it hands to the broadcaster. Deletions are
///     covered for every type, together with the tombstone each one leaves.
/// </summary>
public sealed class ClusterConfigSyncLiveEnvelopeTests : IDisposable
{
    private static readonly Guid OwnNode = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid OtherNode = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly DateTimeOffset Created = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-sync-live-envelope-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly ServiceProvider _services;
    private readonly RecordingBroadcaster _broadcaster = new();
    private readonly IDataProtectionProvider _dataProtection =
        new EphemeralDataProtectionProvider();

    public ClusterConfigSyncLiveEnvelopeTests()
    {
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddDbContext<NinlilDnsContext>(options =>
            options.UseNinlilDnsSqlite($"Data Source={_databasePath}")
        );
        collection.AddScoped<IDnsFilterListRepository, DnsFilterListRepository>();
        collection.AddScoped<IDnsFilterListEntryRepository, DnsFilterListEntryRepository>();
        collection.AddScoped<IDnsAccessControlRuleRepository, DnsAccessControlRuleRepository>();
        collection.AddScoped<IDnsRewriteRepository, DnsRewriteRepository>();
        collection.AddScoped<IDnsForwarderRepository, DnsForwarderRepository>();
        collection.AddScoped<IUserRepository, UserRepository>();
        collection.AddScoped<IRoleRepository, RoleRepository>();
        collection.AddScoped<IClusterSyncTombstoneRepository, ClusterSyncTombstoneRepository>();
        collection.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        collection.AddSingleton(_dataProtection);
        collection.AddSingleton<IClusterConfigSyncBroadcaster>(_broadcaster);
        collection.AddSingleton<IOptionsMonitor<ClusterOptions>>(
            new FixedClusterOptions(new ClusterOptions { NodeId = OwnNode, Enable = true })
        );
        _services = collection.BuildServiceProvider();

        using IServiceScope scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    private async Task AddAsync<TEntity>(TEntity entity)
        where TEntity : EntityBase
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        context.Add(entity);
        await context.SaveChangesAsync();
    }

    /// <summary>
    ///     This method hands one event to a real subscriber and waits for the envelope that the
    ///     subscriber's detached task passes to the broadcaster.
    /// </summary>
    private async Task<ClusterConfigSyncEnvelope> SendAsync<TSubscriber, TEvent>(TEvent domainEvent)
        where TSubscriber : IDnsServerEventSubscriber<TEvent>
        where TEvent : IDnsServerEvent
    {
        TSubscriber subscriber = ActivatorUtilities.CreateInstance<TSubscriber>(_services);
        await subscriber.HandleAsync(domainEvent, CancellationToken.None);
        return await _broadcaster.NextAsync();
    }

    private static void AssertEnvelope(
        ClusterConfigSyncEnvelope envelope,
        string entityType,
        Guid id,
        string payloadJson,
        DateTimeOffset lastModifiedUtc,
        Guid originatingNodeId
    )
    {
        Assert.Equal(entityType, envelope.EntityType);
        Assert.Equal(id.ToString(), envelope.EntityKey);
        Assert.False(envelope.IsDeleted);
        Assert.Equal(payloadJson, envelope.PayloadJson);
        Assert.Equal(lastModifiedUtc, envelope.LastModifiedUtc);
        Assert.Equal(originatingNodeId, envelope.OriginatingNodeId);
    }

    [Fact]
    public async Task LiveEnvelope_ForAFilterList_HasTheExpectedWireShape()
    {
        Guid id = Guid.NewGuid();
        await AddAsync(
            new DnsFilterListEntity
            {
                Id = id,
                Name = "Ads",
                Kind = DnsFilterListKindEnum.ResponseIp,
                Action = DnsFilterListActionEnum.Allow,
                Source = DnsFilterListSourceEnum.Subscribed,
                SubscriptionUrl = "https://lists.example.com/ads.txt",
                RefreshInterval = TimeSpan.FromHours(6),
                IsDisabled = true,
                Comments = "c",
                AppliesToAllClients = true,
                CreatedAtUtc = Created,
                UpdatedAtUtc = Updated,
                OriginatingNodeId = OtherNode,
            }
        );

        ClusterConfigSyncEnvelope envelope = await SendAsync<
            FilterListClusterSyncEventSubscriber,
            DnsFilterListChangedEvent
        >(new DnsFilterListChangedEvent(Updated, id));

        AssertEnvelope(
            envelope,
            "FilterList",
            id,
            """{"Name":"Ads","Kind":"ResponseIp","Action":"Allow","Source":"Subscribed","SubscriptionUrl":"https://lists.example.com/ads.txt","RefreshInterval":"06:00:00","IsDisabled":true,"Comments":"c","AppliesToAllClients":true}""",
            Updated,
            OtherNode
        );
    }

    [Fact]
    public async Task LiveEnvelope_ForAFilterListEntryWithoutAnOriginatingNode_StampsThisNode()
    {
        Guid listId = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        await AddAsync(
            new DnsFilterListEntryEntity
            {
                Id = id,
                ListId = listId,
                Value = "ads.example.com",
                Kind = DnsFilterEntryKindEnum.Domain,
                Comment = "e",
                CreatedAtUtc = Created,
            }
        );

        ClusterConfigSyncEnvelope envelope = await SendAsync<
            FilterListEntryClusterSyncEventSubscriber,
            DnsFilterListEntryChangedEvent
        >(new DnsFilterListEntryChangedEvent(Created, listId, id));

        AssertEnvelope(
            envelope,
            "FilterListEntry",
            id,
            $$"""{"ListId":"{{listId}}","Value":"ads.example.com","Kind":"Domain","Comment":"e"}""",
            Created,
            OwnNode
        );
    }

    [Fact]
    public async Task LiveEnvelope_ForAnAccessControlRule_HasTheExpectedWireShape()
    {
        Guid id = Guid.NewGuid();
        await AddAsync(
            new DnsAccessControlRuleEntity
            {
                Id = id,
                Name = "Trusted LAN",
                Cidr = "203.0.113.0/24",
                Capabilities = DnsAccessControlCapabilityEnum.RecursionAccess,
                IsDisabled = false,
                CreatedAtUtc = Created,
                UpdatedAtUtc = Updated,
            }
        );

        ClusterConfigSyncEnvelope envelope = await SendAsync<
            AccessControlRuleClusterSyncEventSubscriber,
            DnsAccessControlRuleChangedEvent
        >(new DnsAccessControlRuleChangedEvent(Updated, id));

        AssertEnvelope(
            envelope,
            "AccessControlRule",
            id,
            """{"Name":"Trusted LAN","Cidr":"203.0.113.0/24","Capabilities":1,"IsDisabled":false}""",
            Updated,
            OwnNode
        );
    }

    [Fact]
    public async Task LiveEnvelope_ForAGlobalRewrite_HasTheExpectedWireShape()
    {
        Guid id = Guid.NewGuid();
        await AddAsync(
            new DnsRewriteEntity
            {
                Id = id,
                Domain = "www.example.com",
                Target = "forcesafesearch.example.net",
                IsDisabled = false,
                Comments = "r",
                CreatedAtUtc = Created,
                OriginatingNodeId = OtherNode,
            }
        );

        ClusterConfigSyncEnvelope envelope = await SendAsync<
            RewriteClusterSyncEventSubscriber,
            DnsRewriteChangedEvent
        >(new DnsRewriteChangedEvent(Created, id));

        AssertEnvelope(
            envelope,
            "Rewrite",
            id,
            """{"Domain":"www.example.com","Target":"forcesafesearch.example.net","IsDisabled":false,"Comments":"r"}""",
            Created,
            OtherNode
        );
    }

    [Fact]
    public async Task LiveEnvelope_ForAForwarder_HasTheExpectedWireShape()
    {
        Guid id = Guid.NewGuid();
        await AddAsync(
            new DnsForwarderEntity
            {
                Id = id,
                Name = "Corp",
                DefinitionString = "corp.example.com=192.0.2.53",
                SortOrder = 3,
                IsDisabled = false,
                Comments = "f",
                CreatedAtUtc = Created,
                UpdatedAtUtc = Updated,
            }
        );

        ClusterConfigSyncEnvelope envelope = await SendAsync<
            ForwarderClusterSyncEventSubscriber,
            DnsForwarderChangedEvent
        >(new DnsForwarderChangedEvent(Updated, id));

        AssertEnvelope(
            envelope,
            "Forwarder",
            id,
            """{"Name":"Corp","DefinitionString":"corp.example.com=192.0.2.53","SortOrder":3,"IsDisabled":false,"Comments":"f"}""",
            Updated,
            OwnNode
        );
    }

    [Fact]
    public async Task LiveEnvelope_ForARoleWithPermissions_HasTheExpectedWireShape()
    {
        Guid id = Guid.NewGuid();
        await AddAsync(
            new RoleEntity
            {
                Id = id,
                Name = "Operators",
                NormalizedName = "OPERATORS",
                Description = "d",
                ConcurrencyStamp = "role-stamp",
                CreatedAtUtc = Created,
                UpdatedAtUtc = Updated,
            }
        );
        await using (AsyncServiceScope scope = _services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IRoleRepository>()
                .AddClaimAsync(id, NinlilDnsClaimTypes.Permission, "dns.zones.read");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        ClusterConfigSyncEnvelope envelope = await SendAsync<
            RoleClusterSyncEventSubscriber,
            RoleChangedEvent
        >(new RoleChangedEvent(Updated, id));

        AssertEnvelope(
            envelope,
            "Role",
            id,
            """{"Name":"Operators","NormalizedName":"OPERATORS","Description":"d","ConcurrencyStamp":"role-stamp","PermissionClaims":["dns.zones.read"]}""",
            Updated,
            OwnNode
        );
    }

    [Fact]
    public async Task LiveEnvelope_ForAUserWithSecrets_CarriesPlaintextSecretsAndNoCiphertext()
    {
        Guid roleId = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        await AddAsync(
            new RoleEntity
            {
                Id = roleId,
                Name = "Operators",
                NormalizedName = "OPERATORS",
                CreatedAtUtc = Created,
            }
        );
        string totpCipher = _dataProtection
            .CreateProtector(NinlilDnsUserStore.AuthenticatorKeyProtectorPurpose)
            .Protect("TOTPSECRET");
        string codesCipher = _dataProtection
            .CreateProtector(NinlilDnsUserStore.RecoveryCodesProtectorPurpose)
            .Protect("code1;code2");
        await AddAsync(
            new UserEntity
            {
                Id = id,
                UserName = "alice",
                NormalizedUserName = "ALICE",
                DisplayName = "Alice Example",
                Email = "alice@example.com",
                NormalizedEmail = "ALICE@EXAMPLE.COM",
                EmailConfirmed = true,
                PasswordHash = "hash",
                SecurityStamp = "sec",
                ConcurrencyStamp = "con",
                TwoFactorEnabled = true,
                TwoFactorTotpSecretEncrypted = totpCipher,
                TwoFactorRecoveryCodesEncrypted = codesCipher,
                LockoutEnabled = true,
                CreatedAtUtc = Created,
                UpdatedAtUtc = Updated,
            }
        );
        await using (AsyncServiceScope scope = _services.CreateAsyncScope())
        {
            IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            await users.AddToRoleAsync(id, roleId);
            await users.AddClaimsAsync(
                id,
                [(NinlilDnsClaimTypes.Permission, "dns.zones.read"), ("other", "ignored")]
            );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        ClusterConfigSyncEnvelope envelope = await SendAsync<
            UserClusterSyncEventSubscriber,
            UserAccountChangedEvent
        >(new UserAccountChangedEvent(Updated, id));

        AssertEnvelope(
            envelope,
            "User",
            id,
            $$"""{"UserName":"alice","NormalizedUserName":"ALICE","DisplayName":"Alice Example","Email":"alice@example.com","NormalizedEmail":"ALICE@EXAMPLE.COM","EmailConfirmed":true,"PasswordHash":"hash","SecurityStamp":"sec","ConcurrencyStamp":"con","PhoneNumber":null,"PhoneNumberConfirmed":false,"TwoFactorEnabled":true,"TwoFactorTotpSecretPlain":"TOTPSECRET","TwoFactorRecoveryCodesPlain":"code1;code2","LockoutEndUtc":null,"LockoutEnabled":true,"AccessFailedCount":0,"IsDisabled":false,"PreferredPalette":null,"PreferredMode":null,"ExternalIdentityProvider":null,"ExternalSubjectId":null,"RoleIds":["{{roleId}}"],"DirectPermissionClaims":["dns.zones.read"]}""",
            Updated,
            OwnNode
        );
        Assert.DoesNotContain(totpCipher, envelope.PayloadJson);
        Assert.DoesNotContain(codesCipher, envelope.PayloadJson);
    }

    [Fact]
    public async Task LiveEnvelope_ForADeletionOfEveryType_IsBareAndLeavesATombstone()
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset at = Updated.AddMinutes(1);
        List<(string Type, ClusterConfigSyncEnvelope Envelope)> sent =
        [
            (
                "FilterList",
                await SendAsync<FilterListClusterSyncEventSubscriber, DnsFilterListChangedEvent>(
                    new DnsFilterListChangedEvent(at, id, true)
                )
            ),
            (
                "FilterListEntry",
                await SendAsync<
                    FilterListEntryClusterSyncEventSubscriber,
                    DnsFilterListEntryChangedEvent
                >(new DnsFilterListEntryChangedEvent(at, Guid.NewGuid(), id, true))
            ),
            (
                "AccessControlRule",
                await SendAsync<
                    AccessControlRuleClusterSyncEventSubscriber,
                    DnsAccessControlRuleChangedEvent
                >(new DnsAccessControlRuleChangedEvent(at, id, true))
            ),
            (
                "Rewrite",
                await SendAsync<RewriteClusterSyncEventSubscriber, DnsRewriteChangedEvent>(
                    new DnsRewriteChangedEvent(at, id, true)
                )
            ),
            (
                "Forwarder",
                await SendAsync<ForwarderClusterSyncEventSubscriber, DnsForwarderChangedEvent>(
                    new DnsForwarderChangedEvent(at, id, true)
                )
            ),
            (
                "Role",
                await SendAsync<RoleClusterSyncEventSubscriber, RoleChangedEvent>(
                    new RoleChangedEvent(at, id, true)
                )
            ),
            (
                "User",
                await SendAsync<UserClusterSyncEventSubscriber, UserAccountChangedEvent>(
                    new UserAccountChangedEvent(at, id, true)
                )
            ),
        ];

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IClusterSyncTombstoneRepository tombstones =
            scope.ServiceProvider.GetRequiredService<IClusterSyncTombstoneRepository>();
        foreach ((string type, ClusterConfigSyncEnvelope envelope) in sent)
        {
            Assert.Equal(type, envelope.EntityType);
            Assert.Equal(id.ToString(), envelope.EntityKey);
            Assert.True(envelope.IsDeleted);
            Assert.Null(envelope.PayloadJson);
            Assert.Equal(at, envelope.LastModifiedUtc);
            Assert.Equal(OwnNode, envelope.OriginatingNodeId);

            ClusterSyncTombstoneEntity tombstone = await tombstones.FindAsync(type, id.ToString());
            Assert.NotNull(tombstone);
            Assert.Equal(at, tombstone.GetLastModifiedUtc());
            Assert.Equal(OwnNode, tombstone.OriginatingNodeId);
        }
    }

    /// <summary>This class records every envelope handed to it and lets a test await the next one.</summary>
    private sealed class RecordingBroadcaster : IClusterConfigSyncBroadcaster
    {
        private readonly System.Threading.Channels.Channel<ClusterConfigSyncEnvelope> _channel =
            System.Threading.Channels.Channel.CreateUnbounded<ClusterConfigSyncEnvelope>();

        public Task BroadcastAsync(
            ClusterConfigSyncEnvelope envelope,
            CancellationToken stoppingToken
        )
        {
            _channel.Writer.TryWrite(envelope);
            return Task.CompletedTask;
        }

        public async Task<ClusterConfigSyncEnvelope> NextAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            return await _channel.Reader.ReadAsync(timeout.Token);
        }
    }

    /// <summary>This class is an options monitor that always returns one value.</summary>
    private sealed class FixedClusterOptions(ClusterOptions value) : IOptionsMonitor<ClusterOptions>
    {
        public ClusterOptions CurrentValue => value;

        public ClusterOptions Get(string name) => value;

        public IDisposable OnChange(Action<ClusterOptions, string> listener) => null;
    }
}
