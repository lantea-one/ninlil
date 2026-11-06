using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Cluster.Backfill;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Http;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests.Backfill;

/// <summary>
///     This class is the service provider the backfill, join and heartbeat tests share: one
///     temporary SQLite database with the cluster repositories, a TSIG secret protector, and the
///     fake sources a test passes in. It creates the schema and deletes the database when
///     disposed, and offers the seeding and lookup helpers those tests repeat.
/// </summary>
internal sealed class ClusterBackfillTestHost : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-backfill-tests-{Guid.NewGuid():N}.sqlite3"
    );

    /// <summary>
    ///     This constructor builds the provider and creates the schema.
    /// </summary>
    /// <param name="sources">The sources the runner resolves for a pass.</param>
    public ClusterBackfillTestHost(params IClusterConfigSyncSource[] sources)
    {
        ServiceCollection collection = new();
        collection.AddDbContext<NinlilDnsContext>(options =>
            options.UseNinlilDnsSqlite($"Data Source={_databasePath}")
        );
        collection.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        collection.AddScoped<IClusterNodeRepository, ClusterNodeRepository>();
        collection.AddScoped<
            IClusterPeerConfigBackfillRepository,
            ClusterPeerConfigBackfillRepository
        >();
        collection.AddScoped<ITsigKeyRepository, TsigKeyRepository>();
        collection.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        collection.AddSingleton<TsigSecretProtector>();
        foreach (IClusterConfigSyncSource source in sources)
            collection.AddSingleton(source);
        Services = collection.BuildServiceProvider();

        using IServiceScope scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();
    }

    /// <summary>This property contains the provider tests resolve scopes and services from.</summary>
    public ServiceProvider Services { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Services.Dispose();
        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    /// <summary>
    ///     This method builds a runner over this database that sends through
    ///     <paramref name="sender" />.
    /// </summary>
    public ClusterConfigBackfillRunner CreateRunner(IClusterConfigPeerSender sender) =>
        new(
            Services.GetRequiredService<IServiceScopeFactory>(),
            sender,
            NullLogger<ClusterConfigBackfillRunner>.Instance
        );

    /// <summary>
    ///     This method saves a peer with a signing key the probe can use, and returns the peer's
    ///     local id.
    /// </summary>
    public async Task<Guid> AddPeerAsync(
        ClusterNodeStatusEnum status = ClusterNodeStatusEnum.Healthy
    )
    {
        using IServiceScope scope = Services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        string keyName = $"cluster-{Guid.NewGuid():N}";
        await provider
            .GetRequiredService<ITsigKeyRepository>()
            .AddAsync(
                new TsigKeyEntity
                {
                    Id = Guid.NewGuid(),
                    Name = keyName,
                    AlgorithmName = "hmac-sha256",
                    SecretEncrypted = provider
                        .GetRequiredService<TsigSecretProtector>()
                        .Protect("a-shared-cluster-channel-secret!"u8.ToArray()),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        ClusterNodeEntity peer = new()
        {
            Id = Guid.NewGuid(),
            Name = $"peer-{Guid.NewGuid():N}"[..13],
            RemoteNodeId = Guid.NewGuid(),
            Host = "127.0.0.1",
            Port = 1,
            TsigKeyName = keyName,
            TlsCertificateThumbprint = new string('a', 64),
            Status = status,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await provider.GetRequiredService<IClusterNodeRepository>().AddAsync(peer);
        await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return peer.Id;
    }

    /// <summary>This method records a backfill request for a peer and returns it.</summary>
    public async Task<ClusterPeerConfigBackfillEntity> AddRequestAsync(Guid peerId)
    {
        using IServiceScope scope = Services.CreateScope();
        ClusterPeerConfigBackfillEntity request = new()
        {
            Id = Guid.NewGuid(),
            ClusterNodeId = peerId,
            RequestedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        await scope
            .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
            .AddAsync(request);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return request;
    }

    /// <summary>This method reads the request of a peer, or null when it has none.</summary>
    public async Task<ClusterPeerConfigBackfillEntity> FindRequestAsync(Guid peerId)
    {
        using IServiceScope scope = Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
            .FindByClusterNodeIdAsync(peerId);
    }

    /// <summary>
    ///     This method deletes a peer row while leaving its request behind, which stands in for an
    ///     engine without cascades. SQLite enforces the foreign key, so it is switched off on one
    ///     connection for the delete.
    /// </summary>
    public async Task DeletePeerKeepingRequestAsync(Guid peerId)
    {
        using IServiceScope scope = Services.CreateScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        string table = context.Model.FindEntityType(typeof(ClusterNodeEntity)).GetTableName();

        await context.Database.OpenConnectionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            // The table name comes from the EF model, not from input, and cannot be a parameter.
#pragma warning disable EF1002
            await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM \"{table}\" WHERE \"Id\" = {{0}}",
                peerId
            );
#pragma warning restore EF1002
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    ///     This class is a source that yields a fixed number of envelopes of one stage, for pass
    ///     tests that care about order and count, not content.
    /// </summary>
    /// <param name="stage">The backfill stage this source reports.</param>
    /// <param name="count">How many envelopes it yields.</param>
    public sealed class FakeSource(int stage, int count) : IClusterConfigSyncSource
    {
        /// <inheritdoc />
        public string EntityType => $"Stage{stage}";

        /// <inheritdoc />
        public int BackfillStage => stage;

        /// <inheritdoc />
        public async IAsyncEnumerable<ClusterConfigSyncEnvelope> ReadBackfillAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken stoppingToken
        )
        {
            for (int index = 0; index < count; index++)
            {
                await Task.Yield();
                yield return new ClusterConfigSyncEnvelope(
                    EntityType,
                    index.ToString(),
                    false,
                    "{}",
                    DateTimeOffset.UtcNow,
                    Guid.Empty
                );
            }
        }
    }

    /// <summary>
    ///     This class is a peer sender that records every delivery and measures how many are in
    ///     flight, and lets a test delay, fail or hook each one.
    /// </summary>
    public sealed class RecordingSender : IClusterConfigPeerSender
    {
        private readonly List<(Guid PeerId, ClusterConfigSyncEnvelope Envelope)> _sent = [];
        private int _inFlight;
        private int _maxInFlight;

        /// <summary>This property contains how long each delivery takes.</summary>
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        /// <summary>This property is called at the start of each delivery; it returns whether the delivery succeeds.</summary>
        public Func<ClusterNodeEntity, ClusterConfigSyncEnvelope, Task<bool>> OnSend { get; set; }

        /// <summary>This property contains the most deliveries that were in flight at once.</summary>
        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        /// <summary>This property contains the deliveries so far, in the order they finished starting.</summary>
        public IReadOnlyList<(Guid PeerId, ClusterConfigSyncEnvelope Envelope)> Sent
        {
            get
            {
                lock (_sent)
                    return [.. _sent];
            }
        }

        /// <inheritdoc />
        public async Task<bool> SendToPeerAsync(
            ClusterNodeEntity peer,
            ClusterConfigSyncEnvelope envelope,
            CancellationToken stoppingToken
        )
        {
            int now = Interlocked.Increment(ref _inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxInFlight)))
                Interlocked.CompareExchange(ref _maxInFlight, now, seen);

            try
            {
                lock (_sent)
                    _sent.Add((peer.Id, envelope));

                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, stoppingToken);

                return OnSend is null || await OnSend(peer, envelope);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    /// <summary>
    ///     This class is a peer HTTP client factory whose clients answer every request with one
    ///     status through a stub handler, so the heartbeat and the join run without a TLS peer. It
    ///     uses the <c>CreateClient</c> test seam.
    /// </summary>
    /// <param name="scopeFactory">The scope factory the base class reads TSIG keys through.</param>
    /// <param name="secretProtector">The protector the base class opens key secrets with.</param>
    /// <param name="status">The status every request gets.</param>
    public sealed class StubClientFactory(
        IServiceScopeFactory scopeFactory,
        TsigSecretProtector secretProtector,
        HttpStatusCode status
    ) : ClusterPeerHttpClientFactory(scopeFactory, secretProtector)
    {
        /// <inheritdoc />
        public override HttpClient CreateClient(
            string pinnedCertificateThumbprintHex,
            TimeSpan timeout
        ) => new(new StubHandler(status)) { Timeout = timeout };
    }

    /// <summary>This class is a message handler that answers every request with one status.</summary>
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken stoppingToken
        ) => Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>
    ///     This class is a backfill runner that records the peers it is asked to start, so a
    ///     heartbeat test sees the call without a pass running.
    /// </summary>
    public sealed class FakeRunner : IClusterConfigBackfillRunner
    {
        private readonly List<Guid> _started = [];

        /// <summary>This property contains the peers <see cref="TryStart" /> was called for, in order.</summary>
        public IReadOnlyList<Guid> Started
        {
            get
            {
                lock (_started)
                    return [.. _started];
            }
        }

        /// <inheritdoc />
        public bool TryStart(Guid clusterNodeId)
        {
            lock (_started)
                _started.Add(clusterNodeId);
            return true;
        }

        /// <inheritdoc />
        public Task<bool> RequestBackfillAsync(
            Guid clusterNodeId,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(true);
    }

    /// <summary>This class is an options monitor that always returns one value.</summary>
    public sealed class FixedOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        /// <inheritdoc />
        public TOptions CurrentValue => value;

        /// <inheritdoc />
        public TOptions Get(string name) => value;

        /// <inheritdoc />
        public IDisposable OnChange(Action<TOptions, string> listener) => null;
    }
}
