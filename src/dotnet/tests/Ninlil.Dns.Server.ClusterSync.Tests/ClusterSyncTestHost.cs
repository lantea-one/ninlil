using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Extension;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class is the service provider the sending-side tests share: one temporary SQLite
///     database with the repositories of every synced entity type, the cluster sync registrations
///     of <see cref="ServiceCollectionClusterSyncExtension" /> (subscribers and sources), a fixed
///     node id, a Data Protection provider, a broadcaster that records what it is given and a
///     logger that keeps its messages. It stands in for the host's composition root so a test
///     drives the real subscribers and sources. The class deletes the database when disposed.
/// </summary>
internal sealed class ClusterSyncTestHost : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-sync-host-tests-{Guid.NewGuid():N}.sqlite3"
    );

    /// <summary>
    ///     This constructor builds the provider and creates the database schema.
    /// </summary>
    /// <param name="ownNodeId">The node id the cluster options report.</param>
    public ClusterSyncTestHost(Guid ownNodeId)
    {
        OwnNodeId = ownNodeId;

        ServiceCollection collection = new();
        collection.AddLogging(builder => builder.AddProvider(Logs));
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
        collection.AddNinlilDnsServerClusterSync();
        collection.AddSingleton(DataProtection);
        collection.AddSingleton<IClusterConfigSyncBroadcaster>(Broadcaster);
        collection.AddSingleton<IOptionsMonitor<ClusterOptions>>(
            new FixedClusterOptions(new ClusterOptions { NodeId = ownNodeId, Enable = true })
        );
        Services = collection.BuildServiceProvider();

        using IServiceScope scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();
    }

    /// <summary>This property contains this node's id, as the cluster options report it.</summary>
    public Guid OwnNodeId { get; }

    /// <summary>This property contains the provider tests resolve scopes and services from.</summary>
    public ServiceProvider Services { get; }

    /// <summary>This property contains the broadcaster the live subscribers hand envelopes to.</summary>
    public RecordingBroadcaster Broadcaster { get; } = new();

    /// <summary>This property contains this node's Data Protection provider.</summary>
    public IDataProtectionProvider DataProtection { get; } = new EphemeralDataProtectionProvider();

    /// <summary>This property contains the logger provider every logger of the host writes to.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <inheritdoc />
    public void Dispose()
    {
        Services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    /// <summary>
    ///     This method inserts a row straight through the context, in its own scope, so a test can
    ///     set every column, the timestamps included.
    /// </summary>
    public async Task AddAsync<TEntity>(TEntity entity)
        where TEntity : EntityBase
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        context.Add(entity);
        await context.SaveChangesAsync();
    }

    /// <summary>
    ///     This method deletes a row straight through the context, in its own scope.
    /// </summary>
    public async Task RemoveAsync<TEntity>(Guid id)
        where TEntity : EntityBase
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Set<TEntity>().Where(entity => entity.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>
    ///     This method hands one event to a real subscriber and waits for the envelope that the
    ///     subscriber's detached task passes to the broadcaster.
    /// </summary>
    public async Task<ClusterConfigSyncEnvelope> SendAsync<TSubscriber, TEvent>(TEvent domainEvent)
        where TSubscriber : IDnsServerEventSubscriber<TEvent>
        where TEvent : IDnsServerEvent
    {
        TSubscriber subscriber = ActivatorUtilities.CreateInstance<TSubscriber>(Services);
        await subscriber.HandleAsync(domainEvent, CancellationToken.None);
        return await Broadcaster.NextAsync();
    }

    /// <summary>
    ///     This class records every envelope handed to it and lets a test await the next one.
    /// </summary>
    public sealed class RecordingBroadcaster : IClusterConfigSyncBroadcaster
    {
        private readonly System.Threading.Channels.Channel<ClusterConfigSyncEnvelope> _channel =
            System.Threading.Channels.Channel.CreateUnbounded<ClusterConfigSyncEnvelope>();

        /// <inheritdoc />
        public Task BroadcastAsync(
            ClusterConfigSyncEnvelope envelope,
            CancellationToken stoppingToken
        )
        {
            _channel.Writer.TryWrite(envelope);
            return Task.CompletedTask;
        }

        /// <summary>This method waits up to ten seconds for the next recorded envelope.</summary>
        public async Task<ClusterConfigSyncEnvelope> NextAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            return await _channel.Reader.ReadAsync(timeout.Token);
        }
    }

    /// <summary>
    ///     This class is a logger provider that keeps the level and the formatted text of every
    ///     message, so a test can assert that a warning names an id.
    /// </summary>
    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        /// <summary>This property contains a snapshot of the messages logged so far.</summary>
        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                    return [.. _entries];
            }
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        /// <inheritdoc />
        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter
            )
            {
                lock (owner._entries)
                    owner._entries.Add((logLevel, formatter(state, exception)));
            }
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
