using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Identity.Authentication;
using Ninlil.Dns.Identity.Token;
using Ninlil.Dns.Server.Cluster.Backfill;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Http;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Extension;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Options;
using Ninlil.Web.Serialization;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class is one cluster node without a web host, for <see cref="ClusterJoinBackfillTests" />:
///     its own temporary SQLite database, its own runtime settings file, its own Data Protection
///     key ring, the real appliers, sources, repositories and settings recorder, and a backfill
///     runner that sends through a <see cref="LoopbackSender" />. A shared test host cannot play
///     two nodes (a second host shares the database and the settings file, and TestServer opens no
///     sockets), so two instances of this class stand in for the inviter and the joiner. It deletes
///     both files when disposed.
/// </summary>
internal sealed class ClusterBackfillNode : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-backfill-node-{Guid.NewGuid():N}.sqlite3"
    );

    /// <summary>
    ///     This constructor builds the node's provider and creates its schema.
    /// </summary>
    /// <param name="nodeId">The node id the cluster options report.</param>
    public ClusterBackfillNode(Guid nodeId)
    {
        NodeId = nodeId;
        SettingsFilePath = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-backfill-node-settings-{Guid.NewGuid():N}.json"
        );

        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddDbContext<NinlilDnsContext>(options =>
            options.UseNinlilDnsSqlite($"Data Source={_databasePath}")
        );
        collection.AddScoped<IDnsFilterListRepository, DnsFilterListRepository>();
        collection.AddScoped<IDnsFilterListEntryRepository, DnsFilterListEntryRepository>();
        collection.AddScoped<
            IDnsClientGroupListAssignmentRepository,
            DnsClientGroupListAssignmentRepository
        >();
        collection.AddScoped<
            IDnsClientListAssignmentRepository,
            DnsClientListAssignmentRepository
        >();
        collection.AddScoped<IDnsAccessControlRuleRepository, DnsAccessControlRuleRepository>();
        collection.AddScoped<IDnsRewriteRepository, DnsRewriteRepository>();
        collection.AddScoped<IDnsForwarderRepository, DnsForwarderRepository>();
        collection.AddScoped<IUserRepository, UserRepository>();
        collection.AddScoped<IRoleRepository, RoleRepository>();
        collection.AddScoped<IClusterSyncTombstoneRepository, ClusterSyncTombstoneRepository>();
        collection.AddScoped<
            IClusterSyncedSettingsStateRepository,
            ClusterSyncedSettingsStateRepository
        >();
        collection.AddScoped<IClusterNodeRepository, ClusterNodeRepository>();
        collection.AddScoped<
            IClusterPeerConfigBackfillRepository,
            ClusterPeerConfigBackfillRepository
        >();
        collection.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        collection.AddScoped<ITsigKeyRepository, TsigKeyRepository>();
        collection.AddSingleton<TsigSecretProtector>();
        collection.AddNinlilDnsServerClusterSync();

        // The settings applier, source and recorder live in Ninlil.Web, as in Program.cs.
        collection.AddScoped<IClusterConfigSyncApplier, SettingsClusterConfigSyncApplier>();
        collection.AddScoped<IClusterConfigSyncSource, SettingsClusterConfigSyncSource>();
        collection.AddScoped<ClusterConfigSyncSettingsRecorder>();
        collection.AddSingleton(
            new RuntimeConfigurationOptions { ConfigurationFilePath = SettingsFilePath }
        );
        collection.AddSingleton<IDnsServerEventPublisher>(
            new SettingsClusterSyncRoundTripTests.CapturingPublisher()
        );
        collection.AddSingleton<IOptionsMonitor<ClusterOptions>>(
            new SettingsClusterSyncRoundTripTests.FakeClusterOptionsMonitor(
                new ClusterOptions { NodeId = nodeId, Enable = true }
            )
        );
        collection.AddSingleton(DataProtection);
        collection.AddSingleton<IAccessTokenRevalidator, NoOpAccessTokenRevalidator>();
        collection.AddSingleton<IJwtTokenService, NoOpJwtTokenService>();
        Services = collection.BuildServiceProvider();

        using IServiceScope scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();
    }

    /// <summary>This property contains the node id the cluster options report.</summary>
    public Guid NodeId { get; }

    /// <summary>This property contains the path of this node's runtime settings file.</summary>
    public string SettingsFilePath { get; }

    /// <summary>This property contains this node's key ring; no other node's ring opens its secrets.</summary>
    public IDataProtectionProvider DataProtection { get; } = new EphemeralDataProtectionProvider();

    /// <summary>This property contains the provider tests resolve scopes from.</summary>
    public ServiceProvider Services { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
        if (File.Exists(SettingsFilePath))
            File.Delete(SettingsFilePath);
    }

    /// <summary>This method inserts a row through the context, so a test sets every column.</summary>
    public async Task AddAsync<TEntity>(TEntity entity)
        where TEntity : EntityBase
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        context.Add(entity);
        await context.SaveChangesAsync();
    }

    /// <summary>This method reads one row by id through the context, or null when it is missing.</summary>
    public async Task<TEntity> FindAsync<TEntity>(Guid id)
        where TEntity : EntityBase
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        return await context.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == id);
    }

    /// <summary>This method lists every row of one entity type through the context.</summary>
    public async Task<List<TEntity>> ListAsync<TEntity>()
        where TEntity : EntityBase
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        return await context.Set<TEntity>().AsNoTracking().ToListAsync();
    }

    /// <summary>
    ///     This method reads everything this node would send in a backfill, through the same
    ///     sources the runner uses, sorted by type and key. Reading B this way shows what B holds
    ///     in the wire form, secrets as plaintext opened with B's own key ring.
    /// </summary>
    public async Task<List<ClusterConfigSyncEnvelope>> ReadAllEnvelopesAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        List<ClusterConfigSyncEnvelope> envelopes = [];
        foreach (
            IClusterConfigSyncSource source in scope.ServiceProvider.GetServices<IClusterConfigSyncSource>()
        )
        await foreach (
            ClusterConfigSyncEnvelope envelope in source.ReadBackfillAsync(CancellationToken.None)
        )
            envelopes.Add(envelope);

        return
        [
            .. envelopes
                .OrderBy(e => e.EntityType, StringComparer.Ordinal)
                .ThenBy(e => e.EntityKey, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    ///     This method adds a peer row for another node and returns its local id, the id a
    ///     backfill request names. The peer is healthy, as the inviter records a joiner.
    /// </summary>
    public async Task<Guid> AddPeerAsync(
        Guid remoteNodeId,
        ClusterNodeStatusEnum status = ClusterNodeStatusEnum.Healthy
    )
    {
        // The broadcaster signs each request with the peer's key, so the key must exist locally.
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            ITsigKeyRepository keys =
                scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
            if (await keys.FindByNameAsync("key") is null)
            {
                await keys.AddAsync(
                    new TsigKeyEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = "key",
                        AlgorithmName = "hmac-sha256",
                        SecretEncrypted = scope
                            .ServiceProvider.GetRequiredService<TsigSecretProtector>()
                            .Protect("a-shared-cluster-channel-secret!"u8.ToArray()),
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            }
        }

        ClusterNodeEntity peer = new()
        {
            Id = Guid.NewGuid(),
            Name = "joiner",
            RemoteNodeId = remoteNodeId,
            Host = "joiner.example",
            Port = 443,
            TsigKeyName = "key",
            TlsCertificateThumbprint = "00",
            Status = status,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await AddAsync(peer);
        return peer.Id;
    }

    /// <summary>This method sets the status of a peer row, as a heartbeat round does.</summary>
    public async Task SetPeerStatusAsync(Guid peerId, ClusterNodeStatusEnum status)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        ClusterNodeEntity peer = await context.Set<ClusterNodeEntity>().FindAsync(peerId);
        peer.Status = status;
        await context.SaveChangesAsync();
    }

    /// <summary>
    ///     This method builds the real live broadcaster on this node. Its requests go through
    ///     <paramref name="clientFactory" />, which hands them to the target node's appliers.
    /// </summary>
    public ClusterConfigSyncBroadcaster CreateBroadcaster(
        LoopbackClientFactory clientFactory,
        IClusterConfigBackfillRequester requester
    ) =>
        new(
            Services.GetRequiredService<IServiceScopeFactory>(),
            clientFactory,
            Services.GetRequiredService<IOptionsMonitor<ClusterOptions>>(),
            requester,
            Services.GetRequiredService<ILogger<ClusterConfigSyncBroadcaster>>()
        );

    /// <summary>
    ///     This method builds a runner on this node that sends to <paramref name="sender" />.
    /// </summary>
    public ClusterConfigBackfillRunner CreateRunner(IClusterConfigPeerSender sender) =>
        CreateRunner(
            sender,
            new ClusterConfigBackfillRequester(Services.GetRequiredService<IServiceScopeFactory>())
        );

    /// <summary>
    ///     This method builds a runner on this node that sends to <paramref name="sender" /> and
    ///     records requests through <paramref name="requester" />, which a test shares with the
    ///     broadcaster the way the container shares one requester.
    /// </summary>
    public ClusterConfigBackfillRunner CreateRunner(
        IClusterConfigPeerSender sender,
        IClusterConfigBackfillRequester requester
    ) =>
        new(
            Services.GetRequiredService<IServiceScopeFactory>(),
            sender,
            requester,
            Services.GetRequiredService<ILogger<ClusterConfigBackfillRunner>>()
        );

    /// <summary>
    ///     This method hands one envelope to this node's real applier for its type, the way
    ///     <c>ClusterChannelController.ConfigSync</c> does: a failure is logged and swallowed, and
    ///     the caller always sees success.
    /// </summary>
    public async Task ReceiveAsync(
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IClusterConfigSyncApplier applier = scope
            .ServiceProvider.GetServices<IClusterConfigSyncApplier>()
            .FirstOrDefault(candidate => candidate.EntityType == envelope.EntityType);
        if (applier is null)
            return;

        try
        {
            await applier.ApplyAsync(envelope, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            scope
                .ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger<ClusterBackfillNode>()
                .LogError(exception, "Applying {EntityType} failed.", envelope.EntityType);
        }
    }

    /// <summary>
    ///     This class is the peer sender the backfill runner uses in these tests. It serializes
    ///     each envelope to JSON and back, as the wire does, records the JSON, and hands the copy
    ///     to the target node's real applier. It can be told to refuse every delivery after a
    ///     number of successes, which stands in for a peer that goes away mid-pass.
    /// </summary>
    /// <param name="target">The node that receives the envelopes.</param>
    public sealed class LoopbackSender(ClusterBackfillNode target) : IClusterConfigPeerSender
    {
        /// <summary>
        ///     This field holds the JSON options MVC binds a request body with, built from the same
        ///     configuration <c>Program.cs</c> applies, so the loopback reads an envelope the way
        ///     <c>ClusterChannelController.ConfigSync</c> does (case-insensitive names, declared
        ///     enum names only).
        /// </summary>
        internal static readonly JsonSerializerOptions MvcJsonOptions = new ServiceCollection()
            .AddControllers()
            .AddJsonOptions(MvcJsonSerialization.Configure)
            .Services.BuildServiceProvider()
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.JsonSerializerOptions;

        private readonly List<string> _payloads = [];
        private int _sent;

        /// <summary>This property contains how many deliveries succeed before every later one is refused; null means no limit.</summary>
        public int? SucceedOnly { get; set; }

        /// <summary>This property contains the JSON of every envelope sent, in send order.</summary>
        public IReadOnlyList<string> Payloads
        {
            get
            {
                lock (_payloads)
                    return [.. _payloads];
            }
        }

        /// <inheritdoc />
        public async Task<bool> SendToPeerAsync(
            ClusterNodeEntity peer,
            ClusterConfigSyncEnvelope envelope,
            CancellationToken stoppingToken
        )
        {
            if (SucceedOnly is int limit && Interlocked.Increment(ref _sent) > limit)
                return false;

            string json = JsonSerializer.Serialize(envelope);
            lock (_payloads)
                _payloads.Add(json);

            await target.ReceiveAsync(
                JsonSerializer.Deserialize<ClusterConfigSyncEnvelope>(json, MvcJsonOptions),
                stoppingToken
            );
            return true;
        }
    }

    /// <summary>
    ///     This class is the HTTP client factory of the live broadcaster in these tests. Each
    ///     request it signs is answered by a handler that reads the envelope from the body, as
    ///     <c>ClusterChannelController.ConfigSync</c> does, and hands it to the target node's real
    ///     applier. While <see cref="IsDown" /> is set the handler throws a connection failure, as
    ///     a peer that is off does.
    /// </summary>
    /// <param name="scopeFactory">The scope factory the base class reads TSIG keys through.</param>
    /// <param name="secretProtector">The protector the base class opens key secrets with.</param>
    /// <param name="target">The node that receives the envelopes.</param>
    public sealed class LoopbackClientFactory(
        IServiceScopeFactory scopeFactory,
        TsigSecretProtector secretProtector,
        ClusterBackfillNode target
    ) : ClusterPeerHttpClientFactory(scopeFactory, secretProtector)
    {
        /// <summary>This property makes every request fail to connect.</summary>
        public bool IsDown { get; set; }

        /// <inheritdoc />
        public override HttpClient CreateClient(
            string pinnedCertificateThumbprintHex,
            TimeSpan timeout
        ) => new(new LoopbackHandler(this, target)) { Timeout = timeout };

        private sealed class LoopbackHandler(
            LoopbackClientFactory owner,
            ClusterBackfillNode target
        ) : HttpMessageHandler
        {
            /// <inheritdoc />
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken stoppingToken
            )
            {
                if (owner.IsDown)
                    throw new HttpRequestException("The peer is down.");

                string json = await request.Content.ReadAsStringAsync(stoppingToken);
                await target.ReceiveAsync(
                    JsonSerializer.Deserialize<ClusterConfigSyncEnvelope>(
                        json,
                        LoopbackSender.MvcJsonOptions
                    ),
                    stoppingToken
                );
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            }
        }
    }

    /// <summary>This class is a token revalidator that does nothing; the user applier calls it after each change.</summary>
    private sealed class NoOpAccessTokenRevalidator : IAccessTokenRevalidator
    {
        /// <inheritdoc />
        public Task<bool> RevalidateAsync(
            System.Security.Claims.ClaimsPrincipal principal,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(true);

        /// <inheritdoc />
        public void Invalidate() { }
    }

    /// <summary>This class is a token service that only accepts the revocation the user applier asks for.</summary>
    private sealed class NoOpJwtTokenService : IJwtTokenService
    {
        /// <inheritdoc />
        public Task RevokeAllRefreshTokensAsync(
            Guid userId,
            CancellationToken stoppingToken = default
        ) => Task.CompletedTask;

        /// <inheritdoc />
        public Task<AccessTokenResult> CreateAccessTokenAsync(
            UserEntity user,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<RefreshTokenResult> CreateRefreshTokenAsync(
            Guid userId,
            string ipAddress,
            string userAgent,
            bool isPersistent,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<SessionEntity> ValidateRefreshTokenAsync(
            string rawRefreshToken,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task RevokeRefreshTokenAsync(
            SessionEntity session,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();
    }
}
