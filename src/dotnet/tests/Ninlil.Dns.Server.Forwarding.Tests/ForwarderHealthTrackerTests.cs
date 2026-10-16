using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Definition;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests <see cref="ForwarderHealthTracker" />: the probe interval clamp and the
///     loop that must survive a value <c>Task.Delay</c> cannot wait for (the real background
///     service on a <see cref="ManualTimeProvider" />, so nothing sleeps), which forwarders a
///     probe pass reaches (a forwarder set cache over an in-memory SQLite database with the real
///     save interceptor, so a disabled forwarder or a deleted zone is seen at once), the
///     three-failures rule, and the skip of a host name with no known address. Probes go to a
///     fake client that records them; no socket and no DNS lookup is involved.
/// </summary>
public sealed class ForwarderHealthTrackerTests : IDisposable
{
    private const string FirstForwarder = "127.0.0.1:5301";
    private const string SecondForwarder = "127.0.0.1:5302";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingProbeClient _client = new();
    private readonly FakeForwarderHostResolver _hostResolver = new();
    private readonly ForwarderEndpointCache _endpoints;
    private readonly ForwarderSetCache _setCache;
    private readonly UpstreamForwarderDefinitionParser _parser = new();

    public ForwarderHealthTrackerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // The real notifier and save interceptor, so a saved forwarder or zone expires the set
        // cache as it does in the host.
        ServiceCollection services = new();
        services.AddSingleton<EntityChangeNotifier>();
        services.AddSingleton<IEntityChangeNotifier>(provider =>
            provider.GetRequiredService<EntityChangeNotifier>()
        );
        services.AddSingleton<EntityChangeSaveChangesInterceptor>();
        services.AddDbContext<NinlilDnsContext>(
            (provider, builder) =>
                builder
                    .UseSqlite(_connection)
                    .AddInterceptors(
                        provider.GetRequiredService<EntityChangeSaveChangesInterceptor>()
                    )
        );
        services.AddScoped<IZoneRepository, ZoneRepository>();
        services.AddScoped<IDnsForwarderRepository, DnsForwarderRepository>();
        services.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        _provider = services.BuildServiceProvider();

        using (IServiceScope scope = _provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();

        _setCache = new ForwarderSetCache(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<IEntityChangeNotifier>(),
            NullLogger<ForwarderSetCache>.Instance
        );
        _endpoints = new ForwarderEndpointCache(
            _parser,
            _hostResolver,
            NullLogger<ForwarderEndpointCache>.Instance,
            _clock
        );
    }

    public void Dispose()
    {
        _setCache.Dispose();
        _provider.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    ///     This class is a plain-protocol forwarder client that records every probe it is given
    ///     and fails them all while <see cref="Failing" /> is set. It stands in for the network.
    /// </summary>
    private sealed class RecordingProbeClient : IUpstreamForwarderClient
    {
        private readonly List<string> _probed = [];

        /// <summary>
        ///     This property is whether the next probes fail with an exception.
        /// </summary>
        public bool Failing { get; set; }

        /// <summary>
        ///     This property is the definition strings probed so far, in call order.
        /// </summary>
        public IReadOnlyList<string> Probed
        {
            get
            {
                lock (_probed)
                    return [.. _probed];
            }
        }

        /// <inheritdoc />
        public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Plain;

        /// <inheritdoc />
        public Task<DnsMessage> ForwardAsync(
            UpstreamForwarderDefinition definition,
            DnsMessage query,
            CancellationToken stoppingToken = default
        )
        {
            lock (_probed)
                _probed.Add(definition.OriginalDefinitionString);

            return Failing
                ? Task.FromException<DnsMessage>(new IOException("probe failed"))
                : Task.FromResult(new DnsMessage { IsResponse = true });
        }
    }

    /// <summary>
    ///     This method builds a tracker over the test's set cache, endpoint cache, clock and
    ///     recording client.
    /// </summary>
    private ForwarderHealthTracker CreateTracker(ForwardingOptions options = null) =>
        new(
            [_client],
            new StaticOptionsMonitor<ForwardingOptions>(
                options
                    ?? new ForwardingOptions
                    {
                        Enable = true,
                        FailoverStrategy = ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin,
                    }
            ),
            NullLogger<ForwarderHealthTracker>.Instance,
            _setCache,
            _endpoints,
            _clock
        );

    /// <summary>
    ///     This method saves a global forwarder with the given definition string and returns its id.
    /// </summary>
    private async Task<Guid> SeedForwarderAsync(string definitionString)
    {
        using IServiceScope scope = _provider.CreateScope();
        Guid id = Guid.NewGuid();
        await scope
            .ServiceProvider.GetRequiredService<IDnsForwarderRepository>()
            .AddAsync(
                new DnsForwarderEntity
                {
                    Id = id,
                    Name = $"forwarder-{id:N}",
                    DefinitionString = definitionString,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return id;
    }

    /// <summary>
    ///     This method disables the global forwarder with that id and saves.
    /// </summary>
    private async Task DisableForwarderAsync(Guid id)
    {
        using IServiceScope scope = _provider.CreateScope();
        IDnsForwarderRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();
        DnsForwarderEntity forwarder = (await repository.ListAsync()).Single(entity =>
            entity.Id == id
        );
        forwarder.IsDisabled = true;
        repository.Update(forwarder);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method saves a forwarder zone that forwards to one address.
    /// </summary>
    private async Task SeedForwarderZoneAsync(string zoneName, string address)
    {
        using IServiceScope scope = _provider.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .AddAsync(
                new ZoneEntity
                {
                    Id = Guid.NewGuid(),
                    Name = zoneName,
                    Type = ZoneTypeEnum.Forwarder,
                    ForwarderAddresses = [address],
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method deletes the zone with that name and saves.
    /// </summary>
    private async Task DeleteZoneAsync(string zoneName)
    {
        using IServiceScope scope = _provider.CreateScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        repository.Remove(await repository.FindByNameAsync(zoneName));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method makes the tracker know a forwarder, as the resolver does when it orders it.
    /// </summary>
    private UpstreamForwarderDefinition Track(ForwarderHealthTracker tracker, string definition)
    {
        UpstreamForwarderDefinition parsed = _parser.Parse(definition);
        tracker.EnsureTracked(parsed);

        return parsed;
    }

    /// <summary>
    ///     This method waits until the background loop has created its next timer, which means it
    ///     is waiting on the manual clock again. It polls with a yield and gives up after ten
    ///     seconds so a broken loop fails the test and does not hang it.
    /// </summary>
    private async Task WaitForTimersAsync(int count)
    {
        DateTime giveUp = DateTime.UtcNow.AddSeconds(10);

        while (_clock.TimersCreated < count)
        {
            Assert.True(DateTime.UtcNow < giveUp, "The loop never reached its next wait.");
            await Task.Delay(1);
        }
    }

    [Theory]
    [InlineData("-00:00:05", "00:00:01")]
    [InlineData("00:00:00", "00:00:01")]
    [InlineData("00:00:00.500", "00:00:01")]
    [InlineData("00:00:30", "00:00:30")]
    [InlineData("1.00:00:00", "1.00:00:00")]
    [InlineData("49.17:02:47.295", "1.00:00:00")]
    [InlineData("10675199.02:48:05", "1.00:00:00")]
    public void ClampProbeInterval_ForAnyConfiguredValue_ReturnsAValueTaskDelayAccepts(
        string configured,
        string expected
    ) =>
        Assert.Equal(
            TimeSpan.Parse(expected),
            ForwarderHealthTracker.ClampProbeInterval(TimeSpan.Parse(configured))
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WithANegativeHealthCheckInterval_KeepsRunning(
        bool probingEnabled
    )
    {
        // With probing off, the loop only waits; with it on, the loop waits and then probes.
        // Both paths pass the interval to Task.Delay. With probing off nothing catches its
        // exception, so an unchecked negative value would end the loop.
        using ForwarderHealthTracker tracker = CreateTracker(
            new ForwardingOptions
            {
                Enable = probingEnabled,
                FailoverStrategy = ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin,
                HealthCheckInterval = TimeSpan.FromSeconds(-5),
            }
        );

        await tracker.StartAsync(CancellationToken.None);
        await WaitForTimersAsync(1);

        // The clamped one-second wait ends and the loop waits again.
        _clock.Advance(ForwarderHealthTracker.MinimumProbeInterval);
        await WaitForTimersAsync(2);

        Assert.NotNull(tracker.ExecuteTask);
        Assert.False(tracker.ExecuteTask.IsCompleted);

        await tracker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProbeAllAsync_WithAForwarderStillConfigured_ProbesEachTrackedForwarder()
    {
        await SeedForwarderAsync(FirstForwarder);
        await SeedForwarderAsync(SecondForwarder);
        ForwarderHealthTracker tracker = CreateTracker();
        Track(tracker, FirstForwarder);
        Track(tracker, SecondForwarder);

        await tracker.ProbeAllAsync(CancellationToken.None);

        Assert.Equal([FirstForwarder, SecondForwarder], _client.Probed.Order());
    }

    [Fact]
    public async Task ProbeAllAsync_AfterAForwarderIsDisabled_ProbesOnlyTheOther()
    {
        Guid first = await SeedForwarderAsync(FirstForwarder);
        await SeedForwarderAsync(SecondForwarder);
        ForwarderHealthTracker tracker = CreateTracker();
        Track(tracker, FirstForwarder);
        Track(tracker, SecondForwarder);

        await DisableForwarderAsync(first);
        await tracker.ProbeAllAsync(CancellationToken.None);

        Assert.Equal([SecondForwarder], _client.Probed);

        // A forgotten forwarder starts fresh: its failure history does not survive.
        Assert.True(tracker.IsHealthy(FirstForwarder));
    }

    [Fact]
    public async Task ProbeAllAsync_AForwarderZoneForwarder_IsProbedWhileTheZoneExistsAndDroppedAfterItIsDeleted()
    {
        await SeedForwarderZoneAsync("corp.example", FirstForwarder);
        ForwarderHealthTracker tracker = CreateTracker();
        Track(tracker, FirstForwarder);

        await tracker.ProbeAllAsync(CancellationToken.None);
        Assert.Equal([FirstForwarder], _client.Probed);

        await DeleteZoneAsync("corp.example");
        await tracker.ProbeAllAsync(CancellationToken.None);

        Assert.Equal([FirstForwarder], _client.Probed);
    }

    [Fact]
    public void RecordResult_OneFailedQuery_KeepsTheForwarderInTheAttemptPool()
    {
        ForwarderHealthTracker tracker = CreateTracker();
        UpstreamForwarderDefinition first = Track(tracker, FirstForwarder);
        UpstreamForwarderDefinition second = Track(tracker, SecondForwarder);

        tracker.RecordResult(first, succeeded: false);

        Assert.True(tracker.IsHealthy(FirstForwarder));
        Assert.Contains(first, tracker.OrderForAttempt([first, second]));
    }

    [Fact]
    public void RecordResult_ThreeFailuresInARow_ExcludeTheForwarderAndOneSuccessBringsItBack()
    {
        ForwarderHealthTracker tracker = CreateTracker();
        UpstreamForwarderDefinition first = Track(tracker, FirstForwarder);
        UpstreamForwarderDefinition second = Track(tracker, SecondForwarder);

        for (
            int failure = 0;
            failure < ForwarderHealthTracker.UnhealthyAfterConsecutiveFailures;
            failure++
        )
            tracker.RecordResult(first, succeeded: false);

        Assert.False(tracker.IsHealthy(FirstForwarder));
        Assert.Equal([second], tracker.OrderForAttempt([first, second]));

        tracker.RecordResult(first, succeeded: true);

        Assert.True(tracker.IsHealthy(FirstForwarder));
        Assert.Contains(first, tracker.OrderForAttempt([first, second]));
    }

    [Fact]
    public void RecordResult_ASuccessBetweenFailures_RestartsTheCount()
    {
        ForwarderHealthTracker tracker = CreateTracker();
        UpstreamForwarderDefinition first = Track(tracker, FirstForwarder);

        tracker.RecordResult(first, succeeded: false);
        tracker.RecordResult(first, succeeded: false);
        tracker.RecordResult(first, succeeded: true);
        tracker.RecordResult(first, succeeded: false);
        tracker.RecordResult(first, succeeded: false);

        Assert.True(tracker.IsHealthy(FirstForwarder));
    }

    [Fact]
    public async Task ProbeAllAsync_FailingProbes_MarkTheForwarderUnhealthyOnlyOnTheThirdPass()
    {
        await SeedForwarderAsync(FirstForwarder);
        ForwarderHealthTracker tracker = CreateTracker();
        Track(tracker, FirstForwarder);
        _client.Failing = true;

        await tracker.ProbeAllAsync(CancellationToken.None);
        await tracker.ProbeAllAsync(CancellationToken.None);
        Assert.True(tracker.IsHealthy(FirstForwarder));

        await tracker.ProbeAllAsync(CancellationToken.None);
        Assert.False(tracker.IsHealthy(FirstForwarder));

        _client.Failing = false;
        await tracker.ProbeAllAsync(CancellationToken.None);
        Assert.True(tracker.IsHealthy(FirstForwarder));
    }

    [Fact]
    public async Task ProbeAllAsync_AHostNameWithNoKnownAddress_IsSkippedAndNotMarkedUnhealthy()
    {
        const string hostForwarder = "udp://fwd.example:5353";
        await SeedForwarderAsync(hostForwarder);
        ForwarderHealthTracker tracker = CreateTracker();
        Track(tracker, hostForwarder);

        // The fake host resolver leaves the lookup pending, so the address stays unknown.
        for (int pass = 0; pass < ForwarderHealthTracker.UnhealthyAfterConsecutiveFailures; pass++)
            await tracker.ProbeAllAsync(CancellationToken.None);

        Assert.Empty(_client.Probed);
        Assert.True(tracker.IsHealthy(hostForwarder));

        // Once the lookup answers, the forwarder is probed at its address.
        _hostResolver.Complete(0, IPAddress.Loopback);
        await _endpoints.WhenIdleAsync();
        await tracker.ProbeAllAsync(CancellationToken.None);

        Assert.Equal([hostForwarder], _client.Probed);
    }
}
