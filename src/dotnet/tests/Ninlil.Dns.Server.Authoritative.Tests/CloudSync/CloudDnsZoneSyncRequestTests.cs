using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.CloudSync;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class tests <see cref="ICloudZoneSyncTrigger.RequestSync" /> on the running
///     <see cref="CloudDnsZoneSyncService" />. Every zone has a one-hour sync interval, so after the
///     first pass only a request can cause another sync. The provider is an in-memory fake that
///     counts its calls; the repositories run on a temporary SQLite database. Waits poll for an
///     effect with a few seconds as the ceiling, never for the 30 second tick.
/// </summary>
public sealed class CloudDnsZoneSyncRequestTests : IAsyncLifetime
{
    private const string PrimaryProviderZone = "PZ";
    private const string FailingProviderZone = "FZ";
    private const string DisabledProviderZone = "DZ";
    private const string SecondaryProviderZone = "SZ";

    /// <summary>The longest any test waits for an effect it expects.</summary>
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(5);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cloudsync-request-tests-{Guid.NewGuid()}.sqlite3"
    );

    private readonly InMemoryCloudProvider _provider = new();
    private readonly ThreadSafeEventPublisher _publisher = new();
    private readonly CapturingLoggerProvider _logs = new();
    private IHost _host;
    private CloudDnsZoneSyncService _service;
    private Guid _primaryZoneId;
    private Guid _failingZoneId;
    private Guid _disabledZoneId;
    private Guid _secondaryZoneId;

    public async Task InitializeAsync()
    {
        _provider.FailingZones.Add(FailingProviderZone);
        _provider.Seed(
            SecondaryProviderZone,
            new CloudDnsProviderRecord(
                "seed-1",
                CloudDnsRecordMapper.ToWireRecord(
                    DnsResourceRecordTypeEnum.A,
                    "pulled.secondary.example.com",
                    "192.0.2.44",
                    300
                )
            )
        );

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Logging.AddProvider(_logs);
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDnsServerEventPublisher>(_publisher);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddSingleton<CloudCredentialProtector>();
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();
        builder.Services.AddSingleton<AuthoritativeZoneCache>();
        builder.Services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AuthoritativeZoneCache>()
        );
        builder.Services.AddSingleton<ICloudDnsProvider>(_provider);

        _host = builder.Build();

        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
            await SeedAsync(seedScope.ServiceProvider);

        await _host.StartAsync();

        // A new instance, not a registered singleton, so each test controls when the loop starts.
        _service = ActivatorUtilities.CreateInstance<CloudDnsZoneSyncService>(_host.Services);
        await _service.StartAsync(CancellationToken.None);

        // The first pass syncs every enabled zone once: the primary zone pushes its record, the
        // secondary zone pulls, the failing zone fails. Wait for all three effects.
        await WaitUntilAsync(() =>
            _provider.CreateCount(PrimaryProviderZone) == 1
            && _provider.ListCount(SecondaryProviderZone) == 1
            && _publisher.FailedEventCount(_failingZoneId) == 1
        );
    }

    public async Task DisposeAsync()
    {
        _provider.ReleaseGate();
        await _service.StopAsync(CancellationToken.None);
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task RequestSync_ForACloudPrimaryZoneWithALocalChange_PushesItWithinTheBound()
    {
        await AddLocalARecordAsync(_primaryZoneId, "new.primary.example.com", "192.0.2.50");

        _service.RequestSync(_primaryZoneId);

        // The zone's interval is one hour, so only the request can explain a push.
        Assert.True(
            await WaitUntilAsync(() => _provider.CreateCount(PrimaryProviderZone) == 2),
            "The provider did not receive the new record within the bound."
        );
        Assert.True(_provider.Holds(PrimaryProviderZone, "new.primary.example.com"));
    }

    [Fact]
    public async Task RequestSync_WhileASyncIsInFlight_ReturnsAtOnceAndNeverOverlapsTheSync()
    {
        _provider.ArmGate(PrimaryProviderZone);
        int listsBefore = _provider.ListCount(PrimaryProviderZone);

        _service.RequestSync(_primaryZoneId);
        Assert.True(
            await WaitUntilAsync(() => _provider.GateReached),
            "The requested sync did not start."
        );

        // The sync now holds the service's lock and waits in the provider. A second request must
        // return without waiting for it.
        _service.RequestSync(_primaryZoneId);
        Assert.False(_provider.GateReleased);

        _provider.ReleaseGate();

        // The request made during the sync causes exactly one more pass.
        Assert.True(
            await WaitUntilAsync(() => _provider.ListCount(PrimaryProviderZone) == listsBefore + 2),
            "The request made during the sync was not served after it."
        );
        Assert.Equal(1, _provider.MaxConcurrentLists);
    }

    [Fact]
    public async Task RequestSync_CalledTenTimesInABurst_CausesAtMostTwoProviderListCalls()
    {
        int listsBefore = _provider.ListCount(PrimaryProviderZone);

        for (int request = 0; request < 10; request++)
            _service.RequestSync(_primaryZoneId);

        Assert.True(
            await WaitUntilAsync(() => _provider.ListCount(PrimaryProviderZone) > listsBefore),
            "The burst caused no sync."
        );

        // Give a wrongly uncoalesced burst time to show up before counting.
        await Task.Delay(500);

        int syncs = _provider.ListCount(PrimaryProviderZone) - listsBefore;
        Assert.InRange(syncs, 1, 2);
    }

    [Fact]
    public async Task RequestSync_ForAZoneWhoseProviderThrows_WarnsPublishesTheEventAndServesOtherZones()
    {
        await AddLocalARecordAsync(_primaryZoneId, "after.primary.example.com", "192.0.2.51");

        _service.RequestSync(_failingZoneId);
        _service.RequestSync(_primaryZoneId);

        Assert.True(
            await WaitUntilAsync(() => _publisher.FailedEventCount(_failingZoneId) == 2),
            "The failed request did not publish its event."
        );
        Assert.True(
            await WaitUntilAsync(() =>
                _provider.Holds(PrimaryProviderZone, "after.primary.example.com")
            ),
            "The other zone was not served after the failure."
        );
        Assert.Equal(2, _logs.WarningCount("failing.example.com"));
    }

    [Fact]
    public async Task RequestSync_ForADisabledZoneOrAnUnknownId_DoesNothing()
    {
        _service.RequestSync(_disabledZoneId);
        _service.RequestSync(Guid.NewGuid());

        // A third request proves the loop woke and finished its pass with the other two dropped:
        // the secondary zone's pull is the marker.
        _service.RequestSync(_secondaryZoneId);
        Assert.True(
            await WaitUntilAsync(() => _provider.ListCount(SecondaryProviderZone) == 2),
            "The marker request was not served."
        );

        await Task.Delay(300);

        Assert.Equal(0, _provider.ListCount(DisabledProviderZone));
        Assert.Equal(2, _provider.ListCount(SecondaryProviderZone));
        Assert.Equal(1, _provider.ListCount(PrimaryProviderZone));
    }

    [Fact]
    public async Task RequestSync_ForACloudSecondaryZone_PullsItAgain()
    {
        _service.RequestSync(_secondaryZoneId);

        Assert.True(
            await WaitUntilAsync(() => _provider.ListCount(SecondaryProviderZone) == 2),
            "The secondary zone was not pulled again."
        );
    }

    /// <summary>
    ///     This method polls until a condition holds, for at most <see cref="Ceiling" />. The
    ///     ceiling bounds a failure; a passing test returns as soon as the effect is seen.
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + Ceiling;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(20);
        }

        return condition();
    }

    /// <summary>This method adds one A record to a zone through the journalled update service.</summary>
    private async Task AddLocalARecordAsync(Guid zoneId, string name, string address)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneUpdateService updateService =
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();

        await updateService.ApplyChangesAsync(
            zoneId,
            [
                CreateRecord(
                    zoneId,
                    name,
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(IPAddress.Parse(address))
                ),
            ],
            []
        );
    }

    private static RecordEntity CreateRecord<TData>(
        Guid zoneId,
        string name,
        DnsResourceRecordTypeEnum type,
        TData data
    )
        where TData : IDnsRecordData =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = name,
            Type = type,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = 300,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(typeof(TData), data),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>
    ///     This method seeds the credential and four zones: a CloudPrimary zone with an SOA and one
    ///     record, a CloudPrimary zone whose provider throws, a disabled CloudPrimary zone and a
    ///     CloudSecondary zone with only its SOA. All have a one-hour interval.
    /// </summary>
    private async Task SeedAsync(IServiceProvider services)
    {
        NinlilDnsContext context = services.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository = services.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository = services.GetRequiredService<IRecordRepository>();
        ICloudDnsProviderCredentialRepository credentialRepository =
            services.GetRequiredService<ICloudDnsProviderCredentialRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();
        CloudCredentialProtector protector =
            services.GetRequiredService<CloudCredentialProtector>();

        Guid credentialId = Guid.NewGuid();
        await credentialRepository.AddAsync(
            new CloudDnsProviderCredentialEntity
            {
                Id = credentialId,
                Name = "in-memory",
                ProviderType = CloudDnsProviderTypeEnum.Cloudflare,
                CredentialsEncrypted = protector.Protect(
                    new Dictionary<string, string> { ["ApiToken"] = "fake" }
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        _primaryZoneId = Guid.NewGuid();
        _failingZoneId = Guid.NewGuid();
        _disabledZoneId = Guid.NewGuid();
        _secondaryZoneId = Guid.NewGuid();

        async Task AddZoneAsync(
            Guid id,
            string name,
            ZoneTypeEnum type,
            string providerZone,
            bool isDisabled
        ) =>
            await zoneRepository.AddAsync(
                new ZoneEntity
                {
                    Id = id,
                    Name = name,
                    Type = type,
                    IsDisabled = isDisabled,
                    CloudProviderCredentialId = credentialId,
                    CloudProviderZoneId = providerZone,
                    CloudSyncInterval = TimeSpan.FromHours(1),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        await AddZoneAsync(
            _primaryZoneId,
            "primary.example.com",
            ZoneTypeEnum.CloudPrimary,
            PrimaryProviderZone,
            false
        );
        await AddZoneAsync(
            _failingZoneId,
            "failing.example.com",
            ZoneTypeEnum.CloudPrimary,
            FailingProviderZone,
            false
        );
        await AddZoneAsync(
            _disabledZoneId,
            "disabled.example.com",
            ZoneTypeEnum.CloudPrimary,
            DisabledProviderZone,
            true
        );
        await AddZoneAsync(
            _secondaryZoneId,
            "secondary.example.com",
            ZoneTypeEnum.CloudSecondary,
            SecondaryProviderZone,
            false
        );
        await unitOfWork.SaveChangesAsync();

        foreach (string zone in new[] { "primary", "secondary" })
        {
            await recordRepository.AddAsync(
                CreateRecord(
                    zone == "primary" ? _primaryZoneId : _secondaryZoneId,
                    $"{zone}.example.com",
                    DnsResourceRecordTypeEnum.Soa,
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{zone}.example.com"),
                        DnsName.Parse($"admin.{zone}.example.com"),
                        1,
                        3600,
                        900,
                        604800,
                        3600
                    )
                )
            );
        }

        await recordRepository.AddAsync(
            CreateRecord(
                _primaryZoneId,
                "www.primary.example.com",
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse("192.0.2.10"))
            )
        );
        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This fake keeps each provider zone's records in memory, counts the calls per zone, throws
    ///     for zones listed in <see cref="FailingZones" /> and can hold one zone's list call at a
    ///     gate, so a test can request a sync while another is still in flight.
    /// </summary>
    private sealed class InMemoryCloudProvider : ICloudDnsProvider
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, List<CloudDnsProviderRecord>> _records = [];
        private readonly ConcurrentDictionary<string, int> _lists = new();
        private readonly ConcurrentDictionary<string, int> _creates = new();
        private TaskCompletionSource _gate;
        private string _gatedZone;
        private int _concurrentLists;

        /// <summary>The provider zones whose list call throws.</summary>
        public HashSet<string> FailingZones { get; } = [];

        /// <summary>Whether a list call reached the armed gate.</summary>
        public bool GateReached { get; private set; }

        /// <summary>Whether the gate has been released.</summary>
        public bool GateReleased => _gate is null || _gate.Task.IsCompleted;

        /// <summary>The highest number of list calls that ran at the same time.</summary>
        public int MaxConcurrentLists { get; private set; }

        /// <inheritdoc />
        public CloudDnsProviderTypeEnum ProviderType => CloudDnsProviderTypeEnum.Cloudflare;

        /// <summary>This method adds a record to a provider zone before the test starts.</summary>
        public void Seed(string providerZone, CloudDnsProviderRecord record)
        {
            lock (_sync)
                RecordsOf(providerZone).Add(record);
        }

        /// <summary>This method makes the next list call for a zone wait until <see cref="ReleaseGate" />.</summary>
        public void ArmGate(string providerZone)
        {
            _gatedZone = providerZone;
            GateReached = false;
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>This method lets a waiting list call continue and disarms the gate.</summary>
        public void ReleaseGate()
        {
            _gatedZone = null;
            _gate?.TrySetResult();
        }

        /// <summary>This method returns how often a zone was listed.</summary>
        public int ListCount(string providerZone) => _lists.GetValueOrDefault(providerZone);

        /// <summary>This method returns how many records were created in a zone.</summary>
        public int CreateCount(string providerZone) => _creates.GetValueOrDefault(providerZone);

        /// <summary>This method tells whether a zone holds a record with the given owner name.</summary>
        public bool Holds(string providerZone, string name)
        {
            lock (_sync)
                return RecordsOf(providerZone)
                    .Any(record =>
                        string.Equals(
                            record.Record.Name.ToString().TrimEnd('.'),
                            name,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<CloudDnsProviderRecord>> ListRecordsAsync(
            IReadOnlyDictionary<string, string> credentials,
            string providerZoneId,
            CancellationToken stoppingToken = default
        )
        {
            int concurrent = Interlocked.Increment(ref _concurrentLists);

            try
            {
                lock (_sync)
                    MaxConcurrentLists = Math.Max(MaxConcurrentLists, concurrent);

                _lists.AddOrUpdate(providerZoneId, 1, (_, count) => count + 1);

                if (FailingZones.Contains(providerZoneId))
                    throw new InvalidOperationException("The provider is down.");

                if (providerZoneId == _gatedZone && _gate is { } gate)
                {
                    GateReached = true;
                    await gate.Task.WaitAsync(stoppingToken);
                }

                lock (_sync)
                    return [.. RecordsOf(providerZoneId)];
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentLists);
            }
        }

        /// <inheritdoc />
        public Task CreateRecordAsync(
            IReadOnlyDictionary<string, string> credentials,
            string providerZoneId,
            DnsResourceRecord record,
            CancellationToken stoppingToken = default
        )
        {
            lock (_sync)
                RecordsOf(providerZoneId)
                    .Add(new CloudDnsProviderRecord(Guid.NewGuid().ToString(), record));

            _creates.AddOrUpdate(providerZoneId, 1, (_, count) => count + 1);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteRecordAsync(
            IReadOnlyDictionary<string, string> credentials,
            string providerZoneId,
            CloudDnsProviderRecord existingRecord,
            CancellationToken stoppingToken = default
        )
        {
            lock (_sync)
                RecordsOf(providerZoneId)
                    .RemoveAll(record =>
                        record.ProviderRecordId == existingRecord.ProviderRecordId
                    );

            return Task.CompletedTask;
        }

        private List<CloudDnsProviderRecord> RecordsOf(string providerZone)
        {
            if (!_records.TryGetValue(providerZone, out List<CloudDnsProviderRecord> records))
                _records[providerZone] = records = [];

            return records;
        }
    }

    /// <summary>
    ///     This fake collects published events behind a lock, because the sync loop publishes from
    ///     its own thread while a test reads.
    /// </summary>
    private sealed class ThreadSafeEventPublisher : IDnsServerEventPublisher
    {
        private readonly List<IDnsServerEvent> _published = [];

        /// <inheritdoc />
        public void Publish(IDnsServerEvent domainEvent)
        {
            lock (_published)
                _published.Add(domainEvent);
        }

        /// <summary>This method counts the sync-failed events published for a zone.</summary>
        public int FailedEventCount(Guid zoneId)
        {
            lock (_published)
                return _published
                    .OfType<CloudDnsZoneSyncFailedEvent>()
                    .Count(failed => failed.ZoneId == zoneId);
        }
    }

    /// <summary>
    ///     This logger provider keeps every Warning-level message, so a test can count the warnings
    ///     about one zone.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _warnings = [];

        /// <summary>This method counts the warnings whose text contains a fragment.</summary>
        public int WarningCount(string fragment)
        {
            lock (_warnings)
                return _warnings.Count(message => message.Contains(fragment));
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_warnings);

        /// <inheritdoc />
        public void Dispose() { }

        /// <summary>This logger writes Warning and higher messages to the shared list.</summary>
        private sealed class CapturingLogger(List<string> warnings) : ILogger
        {
            /// <inheritdoc />
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter
            )
            {
                if (logLevel < LogLevel.Warning)
                    return;

                lock (warnings)
                    warnings.Add(formatter(state, exception));
            }
        }
    }
}
