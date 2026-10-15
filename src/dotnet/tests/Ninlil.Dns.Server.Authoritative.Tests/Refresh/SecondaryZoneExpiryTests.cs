using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Refresh;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Tsig;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Refresh;

/// <summary>
///     This class covers SOA Expire and Retry at a secondary (RFC 1035 §4.3.5): an expired zone
///     answers SERVFAIL with AA clear and refuses a transfer with a signed SERVFAIL, the state
///     survives a restart through the stored refresh time, SOA Expire 0 means no expiry, and the
///     next check after a failure follows the SOA Retry field. Each test builds a SQLite host with a
///     <see cref="ManualTimeProvider" />, a scripted <see cref="IZoneTransferClient" /> in place of
///     the real ones and a refresh service that only loads its state at start; the test drives the
///     passes itself through <c>CheckDueZonesAsync</c>, so nothing waits on a wall clock or races a
///     background loop.
/// </summary>
public sealed class SecondaryZoneExpiryTests
{
    private const string ZoneName = "exp.test";

    /// <summary>
    ///     This class is a transfer client for the Plain transport whose primary the test controls:
    ///     the SOA serial it reports (or <see langword="null" /> for an unreachable primary). A pull
    ///     is never needed because the reported serial never exceeds the local one.
    /// </summary>
    private sealed class ScriptedZoneTransferClient : IZoneTransferClient
    {
        /// <summary>The serial the primary reports, or <see langword="null" /> when unreachable.</summary>
        public uint? SoaSerial { get; set; }

        /// <inheritdoc />
        public ZoneTransferTransportEnum Transport => ZoneTransferTransportEnum.Plain;

        /// <inheritdoc />
        public Task<uint?> QuerySoaSerialAsync(
            IPAddress primaryAddress,
            DnsName zoneName,
            string tsigKeyName,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(SoaSerial);

        /// <inheritdoc />
        public Task<ZoneTransferPullResult> PullAsync(
            IPAddress primaryAddress,
            DnsName zoneName,
            uint? currentSerial,
            string tsigKeyName,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(ZoneTransferPullResult.Failure("The scripted client does not pull."));
    }

    /// <summary>
    ///     This class replaces the refresh service's hosted-service registration: starting it only
    ///     loads the stored state (what <c>SecondaryZoneRefreshService.StartAsync</c> does before the
    ///     loop starts), so the loop never runs beside the passes a test drives by hand.
    /// </summary>
    /// <param name="service">The real refresh service.</param>
    private sealed class LoadOnlyHostedService(SecondaryZoneRefreshService service) : IHostedService
    {
        /// <inheritdoc />
        public Task StartAsync(CancellationToken stoppingToken) =>
            service.LoadExpiryStateAsync(stoppingToken);

        /// <inheritdoc />
        public Task StopAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    /// <summary>
    ///     This class is a logger provider that keeps the formatted messages of the refresh
    ///     service, with their level.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        /// <summary>The captured messages; guarded by a lock on the list itself.</summary>
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(SecondaryZoneRefreshService).FullName
                ? new CapturingLogger(this)
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        /// <summary>This method returns the captured messages of one level.</summary>
        public List<string> At(LogLevel level)
        {
            lock (Entries)
                return [.. Entries.Where(e => e.Level == level).Select(e => e.Message)];
        }

        /// <inheritdoc />
        public void Dispose() { }

        /// <summary>This class writes the messages of one category into the provider's list.</summary>
        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            /// <inheritdoc />
            public IDisposable BeginScope<TState>(TState state) => null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) => true;

            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter
            )
            {
                lock (owner.Entries)
                    owner.Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    /// <summary>
    ///     This class is one test's host: the secondary zone <see cref="ZoneName" /> (SOA Refresh
    ///     600, Retry 120, serial 1, a <c>www</c> A record, primary 127.0.0.1), the manual clock, the
    ///     scripted primary and the captured log. Seeding happens before the host starts, as the
    ///     database would exist before a restart.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private AuthoritativeTestZoneHost _host;

        /// <summary>The clock every component of the host reads.</summary>
        public ManualTimeProvider Clock { get; } = new();

        /// <summary>The scripted primary; unreachable until a test sets its serial.</summary>
        public ScriptedZoneTransferClient Primary { get; } = new();

        /// <summary>The refresh service's captured log.</summary>
        public CapturingLoggerProvider Log { get; } = new();

        /// <summary>The identifier of the secondary zone.</summary>
        public Guid ZoneId { get; } = Guid.NewGuid();

        /// <summary>The name of the TSIG key bound to the zone's transfers.</summary>
        public string KeyName { get; } = $"expiry-key-{Guid.NewGuid()}";

        /// <summary>The host's services.</summary>
        public IServiceProvider Services => _host.Services;

        /// <summary>The refresh service under test.</summary>
        public SecondaryZoneRefreshService Service =>
            Services.GetRequiredService<SecondaryZoneRefreshService>();

        /// <summary>The expiry tracker the resolver reads.</summary>
        public SecondaryZoneExpiryTracker Tracker =>
            Services.GetRequiredService<SecondaryZoneExpiryTracker>();

        /// <summary>
        ///     This method builds the host and seeds the zone without starting it.
        /// </summary>
        /// <param name="expireSeconds">The SOA Expire field.</param>
        /// <param name="hasSoa">Whether the zone has a local SOA yet.</param>
        /// <param name="zoneType">The type of the seeded zone.</param>
        /// <param name="rowAgeSeconds">
        ///     The age of the stored refresh time at seeding, or <see langword="null" /> for no row.
        /// </param>
        public static async Task<Harness> CreateAsync(
            uint expireSeconds = 3600,
            bool hasSoa = true,
            ZoneTypeEnum zoneType = ZoneTypeEnum.Secondary,
            int? rowAgeSeconds = 0
        )
        {
            Harness harness = new();
            harness._host = await AuthoritativeTestZoneHost.CreateAsync(
                harness.Log,
                services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(harness.Clock);
                    services.RemoveAll<IZoneTransferClient>();
                    services.AddSingleton<IZoneTransferClient>(harness.Primary);

                    // Swap the refresh service's hosted-service registration for the load-only one.
                    for (int i = 0; i < services.Count; i++)
                    {
                        ServiceDescriptor descriptor = services[i];
                        if (
                            descriptor.ServiceType != typeof(IHostedService)
                            || descriptor.ImplementationFactory is not { } factory
                        )
                            continue;

                        services[i] = ServiceDescriptor.Describe(
                            typeof(IHostedService),
                            provider =>
                            {
                                object instance = factory(provider);
                                return instance is SecondaryZoneRefreshService refresh
                                    ? new LoadOnlyHostedService(refresh)
                                    : instance;
                            },
                            descriptor.Lifetime
                        );
                    }
                }
            );
            await harness.SeedAsync(expireSeconds, hasSoa, zoneType, rowAgeSeconds);
            return harness;
        }

        private async Task SeedAsync(
            uint expireSeconds,
            bool hasSoa,
            ZoneTypeEnum zoneType,
            int? rowAgeSeconds
        )
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            IServiceProvider provider = scope.ServiceProvider;
            IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
            IDataProtector protector = provider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Ninlil.Dns.Server.Tsig.SharedSecret");

            await provider
                .GetRequiredService<ITsigKeyRepository>()
                .AddAsync(
                    new TsigKeyEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = KeyName,
                        AlgorithmName = "hmac-sha256",
                        SecretEncrypted = protector.Protect(
                            Convert.ToBase64String(
                                new byte[32]
                                    .Select(_ => (byte)7)
                                    .ToArray()
                            )
                        ),
                        IsDisabled = false,
                        CreatedAtUtc = Clock.GetUtcNow(),
                    }
                );
            await provider
                .GetRequiredService<IZoneRepository>()
                .AddAsync(
                    new ZoneEntity
                    {
                        Id = ZoneId,
                        Name = ZoneName,
                        Type = zoneType,
                        PrimaryServerAddresses = ["127.0.0.1"],
                        TransferTransport = ZoneTransferTransportEnum.Plain,
                        TransferTsigKeyNames = [KeyName],
                        CreatedAtUtc = Clock.GetUtcNow(),
                    }
                );

            IRecordRepository records = provider.GetRequiredService<IRecordRepository>();
            if (hasSoa)
                await records.AddAsync(
                    NewRecord(
                        ZoneName,
                        DnsResourceRecordTypeEnum.Soa,
                        new SoaRecordData(
                            DnsName.Parse($"ns1.{ZoneName}"),
                            DnsName.Parse($"admin.{ZoneName}"),
                            1,
                            600,
                            120,
                            expireSeconds,
                            300
                        )
                    )
                );
            await records.AddAsync(
                NewRecord(
                    $"www.{ZoneName}",
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(IPAddress.Parse("192.0.2.10"))
                )
            );

            if (rowAgeSeconds is { } age)
                await provider
                    .GetRequiredService<ISecondaryZoneRefreshStateRepository>()
                    .AddAsync(
                        new SecondaryZoneRefreshStateEntity
                        {
                            Id = Guid.NewGuid(),
                            ZoneId = ZoneId,
                            LastRefreshedAtUtc = Clock.GetUtcNow() - TimeSpan.FromSeconds(age),
                            CreatedAtUtc = Clock.GetUtcNow(),
                        }
                    );

            await unitOfWork.SaveChangesAsync();
        }

        private RecordEntity NewRecord(
            string name,
            DnsResourceRecordTypeEnum type,
            IDnsRecordData data
        ) =>
            new()
            {
                Id = Guid.NewGuid(),
                ZoneId = ZoneId,
                Name = name,
                Type = type,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 300,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(data.GetType(), data),
                CreatedAtUtc = Clock.GetUtcNow(),
            };

        /// <summary>This method starts the host: the caches load and the refresh state loads.</summary>
        public Task StartAsync() => _host.StartAsync();

        /// <summary>This method adds a Primary zone before the host starts.</summary>
        public Task<Guid> AddPrimaryZoneAsync(string name, string address) =>
            _host.AddZoneAsync(name, [TestRecord.A($"www.{name}", address)]);

        /// <summary>This method resolves one A query for <c>www.exp.test</c>.</summary>
        public Task<DnsMessage> QueryWwwAsync() =>
            _host.QueryAsync($"www.{ZoneName}", DnsResourceRecordTypeEnum.A);

        /// <summary>This method resolves one query in the host.</summary>
        public Task<DnsMessage> QueryAsync(string name) =>
            _host.QueryAsync(name, DnsResourceRecordTypeEnum.A);

        /// <summary>This method runs one refresh pass at the current clock time.</summary>
        public Task PassAsync() => Service.CheckDueZonesAsync(CancellationToken.None);

        /// <summary>
        ///     This method reads the stored refresh time of the zone.
        /// </summary>
        /// <returns>The stored time, or <see langword="null" /> if the zone has no row.</returns>
        public async Task<DateTimeOffset?> ReadRowAsync(Guid zoneId)
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            SecondaryZoneRefreshStateEntity row = await scope
                .ServiceProvider.GetRequiredService<ISecondaryZoneRefreshStateRepository>()
                .FindByZoneIdAsync(zoneId);
            return row?.LastRefreshedAtUtc;
        }

        /// <summary>
        ///     This method runs a TSIG-signed AXFR of the zone through
        ///     <see cref="IZoneTransferService" /> and returns every message it yields.
        /// </summary>
        public async Task<List<DnsMessage>> TransferAsync()
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            ITsigService tsig = scope.ServiceProvider.GetRequiredService<ITsigService>();
            DnsMessage query = new()
            {
                Id = 4242,
                Opcode = DnsOpcodeEnum.Query,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse(ZoneName),
                        DnsResourceRecordTypeEnum.Axfr,
                        DnsClassEnum.Internet
                    ),
                },
            };
            DnsMessage signed = await tsig.SignAsync(query, KeyName, query.Id);
            byte[] raw = DnsMessageWriter.Write(signed);
            DnsMessage request = DnsMessageReader.Read(raw, out int tsigOffset);
            DnsQueryContext context = new(
                request,
                DnsTransportProtocolEnum.Tcp,
                IPAddress.Loopback,
                Clock.GetUtcNow(),
                raw,
                tsigOffset
            );

            List<DnsMessage> messages = [];
            await foreach (
                DnsMessage message in scope
                    .ServiceProvider.GetRequiredService<IZoneTransferService>()
                    .CreateAxfrResponseAsync(context)
            )
                messages.Add(message);

            return messages;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
                await _host.DisposeAsync();
        }
    }

    /// <summary>
    ///     A query and a transfer after the Expire interval, with every primary unreachable, get
    ///     SERVFAIL (no AA, no answer; the transfer's reply signed), and one Warning names the zone.
    /// </summary>
    [Fact]
    public async Task Pass_AfterTheExpireIntervalWithNoPrimary_AnswersServfailAndRefusesTransfer()
    {
        await using Harness harness = await Harness.CreateAsync();
        await harness.StartAsync();
        Assert.Equal(DnsResponseCodeEnum.NoError, (await harness.QueryWwwAsync()).ResponseCode);

        harness.Clock.Advance(TimeSpan.FromSeconds(3601));
        await harness.PassAsync();

        DnsMessage response = await harness.QueryWwwAsync();
        Assert.Equal(DnsResponseCodeEnum.ServerFailure, response.ResponseCode);
        Assert.False(response.IsAuthoritativeAnswer);
        Assert.Empty(response.Answers);

        List<DnsMessage> transfer = await harness.TransferAsync();
        DnsMessage only = Assert.Single(transfer);
        Assert.Equal(DnsResponseCodeEnum.ServerFailure, only.ResponseCode);
        Assert.NotNull(only.Tsig);

        string warning = Assert.Single(
            harness.Log.At(LogLevel.Warning),
            m => m.Contains("expired")
        );
        Assert.Contains(ZoneName, warning);
    }

    /// <summary>
    ///     One second before the Expire interval ends, the zone still answers and transfers.
    /// </summary>
    [Fact]
    public async Task Pass_BeforeTheExpireInterval_StillAnswersAndTransfers()
    {
        await using Harness harness = await Harness.CreateAsync();
        await harness.StartAsync();

        harness.Clock.Advance(TimeSpan.FromSeconds(3599));
        await harness.PassAsync();

        DnsMessage response = await harness.QueryWwwAsync();
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.True(response.IsAuthoritativeAnswer);
        Assert.NotEmpty(response.Answers);

        List<DnsMessage> transfer = await harness.TransferAsync();
        Assert.Equal(DnsResponseCodeEnum.NoError, transfer[0].ResponseCode);
        Assert.DoesNotContain(harness.Log.At(LogLevel.Warning), m => m.Contains("expired"));
    }

    /// <summary>
    ///     When a primary confirms the serial after the expiry, the zone answers again, the stored
    ///     time is the new clock value, one Information line says so and the next check follows the
    ///     SOA Refresh field.
    /// </summary>
    [Fact]
    public async Task Pass_WhenAPrimaryConfirmsAnExpiredZone_AnswersAgainAndStoresTheTime()
    {
        await using Harness harness = await Harness.CreateAsync();
        await harness.StartAsync();
        harness.Clock.Advance(TimeSpan.FromSeconds(3601));
        await harness.PassAsync();
        Assert.Equal(
            DnsResponseCodeEnum.ServerFailure,
            (await harness.QueryWwwAsync()).ResponseCode
        );

        harness.Primary.SoaSerial = 1;
        harness.Clock.Advance(TimeSpan.FromSeconds(120));
        await harness.PassAsync();

        DnsMessage response = await harness.QueryWwwAsync();
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.True(response.IsAuthoritativeAnswer);
        Assert.Equal(harness.Clock.GetUtcNow(), await harness.ReadRowAsync(harness.ZoneId));
        Assert.Equal(
            harness.Clock.GetUtcNow() + TimeSpan.FromSeconds(600),
            harness.Service.GetNextCheckAtUtc(harness.ZoneId)
        );
        Assert.Single(harness.Log.At(LogLevel.Information), m => m.Contains("answers again"));
    }

    /// <summary>
    ///     A host that starts after the Expire interval with a stored time older than that answers
    ///     SERVFAIL before any pass has run, which is what a restart with the primaries gone looks
    ///     like.
    /// </summary>
    [Fact]
    public async Task Start_WithAStoredTimeOlderThanExpire_AnswersServfailBeforeAnyPass()
    {
        await using Harness harness = await Harness.CreateAsync();
        harness.Clock.Advance(TimeSpan.FromSeconds(3601));

        await harness.StartAsync();

        DnsMessage response = await harness.QueryWwwAsync();
        Assert.Equal(DnsResponseCodeEnum.ServerFailure, response.ResponseCode);
        Assert.False(response.IsAuthoritativeAnswer);
        Assert.Single(harness.Log.At(LogLevel.Warning), m => m.Contains(ZoneName));
    }

    /// <summary>
    ///     A zone with a local SOA and no stored time gets a row with the current time at start and
    ///     keeps answering: the first start after an upgrade begins a fresh interval.
    /// </summary>
    [Fact]
    public async Task Start_WithNoStoredTime_WritesTheCurrentTimeAndAnswers()
    {
        await using Harness harness = await Harness.CreateAsync(rowAgeSeconds: null);
        Assert.Null(await harness.ReadRowAsync(harness.ZoneId));
        harness.Clock.Advance(TimeSpan.FromSeconds(100000));

        await harness.StartAsync();

        Assert.Equal(harness.Clock.GetUtcNow(), await harness.ReadRowAsync(harness.ZoneId));
        Assert.Equal(DnsResponseCodeEnum.NoError, (await harness.QueryWwwAsync()).ResponseCode);
    }

    /// <summary>
    ///     When every primary fails, the next check is one SOA Retry interval away (120 s here), not
    ///     the default retry interval.
    /// </summary>
    [Fact]
    public async Task Pass_WhenEveryPrimaryFails_SchedulesTheSoaRetryInterval()
    {
        await using Harness harness = await Harness.CreateAsync();
        await harness.StartAsync();

        await harness.PassAsync();

        Assert.Equal(
            harness.Clock.GetUtcNow() + TimeSpan.FromSeconds(120),
            harness.Service.GetNextCheckAtUtc(harness.ZoneId)
        );
    }

    /// <summary>
    ///     A zone with no local SOA has no Retry value, so the default retry interval (30 s) applies.
    /// </summary>
    [Fact]
    public async Task Pass_ForAZoneWithNoLocalSoa_SchedulesTheDefaultRetryInterval()
    {
        await using Harness harness = await Harness.CreateAsync(hasSoa: false);
        await harness.StartAsync();

        await harness.PassAsync();

        Assert.Equal(
            harness.Clock.GetUtcNow() + TimeSpan.FromSeconds(30),
            harness.Service.GetNextCheckAtUtc(harness.ZoneId)
        );
    }

    /// <summary>
    ///     SOA Expire 0 means the zone never expires, however long the primaries stay away, and the
    ///     load logs one Warning.
    /// </summary>
    [Fact]
    public async Task Start_WithSoaExpireZero_NeverExpiresAndWarnsOnce()
    {
        await using Harness harness = await Harness.CreateAsync(expireSeconds: 0);
        await harness.StartAsync();

        harness.Clock.Advance(TimeSpan.FromDays(3650));
        await harness.PassAsync();

        Assert.Equal(DnsResponseCodeEnum.NoError, (await harness.QueryWwwAsync()).ResponseCode);
        Assert.False(harness.Tracker.IsExpired(harness.ZoneId));
        Assert.Single(harness.Log.At(LogLevel.Warning), m => m.Contains("SOA Expire 0"));
    }

    /// <summary>
    ///     A Primary zone and a Stub zone are never marked expired, however much time passes.
    /// </summary>
    [Fact]
    public async Task Pass_ForPrimaryAndStubZones_NeverMarksThemExpired()
    {
        await using Harness stub = await Harness.CreateAsync(zoneType: ZoneTypeEnum.Stub);
        Guid primaryZoneId = await stub.AddPrimaryZoneAsync("prim.test", "192.0.2.77");
        await stub.StartAsync();

        stub.Clock.Advance(TimeSpan.FromDays(30));
        await stub.PassAsync();

        Assert.False(stub.Tracker.IsExpired(stub.ZoneId));
        Assert.False(stub.Tracker.IsExpired(primaryZoneId));
        DnsMessage response = await stub.QueryAsync("www.prim.test");
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.True(response.IsAuthoritativeAnswer);
    }

    /// <summary>
    ///     The expiry after a success is computed from the SOA the zone holds then: with the Expire
    ///     field changed to 60 s the next expiry is 60 s after the refresh. The test edits the stored
    ///     SOA row, which stands for any change of the local SOA (an operator edit of the SOA record).
    /// </summary>
    [Fact]
    public async Task Pass_AfterTheLocalSoaExpireChanges_UsesTheNewExpire()
    {
        await using Harness harness = await Harness.CreateAsync();
        await harness.StartAsync();
        await using (AsyncServiceScope scope = harness.Services.CreateAsyncScope())
        {
            IRecordRepository records =
                scope.ServiceProvider.GetRequiredService<IRecordRepository>();
            RecordEntity soa = (
                await records.FindByNameAsync(
                    harness.ZoneId,
                    ZoneName,
                    DnsResourceRecordTypeEnum.Soa
                )
            ).Single();
            soa.DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                new SoaRecordData(
                    DnsName.Parse($"ns1.{ZoneName}"),
                    DnsName.Parse($"admin.{ZoneName}"),
                    1,
                    600,
                    120,
                    60,
                    300
                )
            );
            records.Update(soa);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        harness.Primary.SoaSerial = 1;
        await harness.PassAsync();

        Assert.Equal(
            harness.Clock.GetUtcNow() + TimeSpan.FromSeconds(60),
            harness.Tracker.GetExpiresAt(harness.ZoneId)
        );
    }

    /// <summary>
    ///     A row whose zone is gone, or is no longer a secondary, is deleted at start; a zone deleted
    ///     while the service runs loses its row, its schedule and its expiry at the next pass.
    /// </summary>
    [Fact]
    public async Task StartAndPass_ForRowsOfGoneZones_DeleteTheRows()
    {
        await using Harness harness = await Harness.CreateAsync();
        Guid primaryZoneId = await harness.AddPrimaryZoneAsync("prim.test", "192.0.2.77");
        Guid unknownZoneId = Guid.NewGuid();
        await using (AsyncServiceScope scope = harness.Services.CreateAsyncScope())
        {
            ISecondaryZoneRefreshStateRepository states =
                scope.ServiceProvider.GetRequiredService<ISecondaryZoneRefreshStateRepository>();
            foreach (Guid zoneId in new[] { primaryZoneId, unknownZoneId })
                await states.AddAsync(
                    new SecondaryZoneRefreshStateEntity
                    {
                        Id = Guid.NewGuid(),
                        ZoneId = zoneId,
                        LastRefreshedAtUtc = harness.Clock.GetUtcNow(),
                        CreatedAtUtc = harness.Clock.GetUtcNow(),
                    }
                );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await harness.StartAsync();

        Assert.Null(await harness.ReadRowAsync(primaryZoneId));
        Assert.Null(await harness.ReadRowAsync(unknownZoneId));
        Assert.NotNull(await harness.ReadRowAsync(harness.ZoneId));

        await harness.PassAsync();
        Assert.NotNull(harness.Service.GetNextCheckAtUtc(harness.ZoneId));
        await using (AsyncServiceScope scope = harness.Services.CreateAsyncScope())
        {
            IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
            zones.Remove(await zones.GetByIdAsync(harness.ZoneId));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await harness.PassAsync();

        Assert.Null(await harness.ReadRowAsync(harness.ZoneId));
        Assert.Null(harness.Service.GetNextCheckAtUtc(harness.ZoneId));
        Assert.Null(harness.Tracker.GetExpiresAt(harness.ZoneId));
    }
}
