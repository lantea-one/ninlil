using System.Net;
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
using Ninlil.Dns.Server.Authoritative.Update;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Core.Resolution;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Update;

/// <summary>
///     This class tests what <see cref="DynamicUpdateQueryResolver" /> does for CloudPrimary zones
///     and for zones without an SOA. It calls the resolver directly with a request whose TSIG
///     outcome is already marked verified (the pipeline does that in production, see
///     <see cref="SignedMessagePipelineTests" />), over the real zone cache, the real
///     <see cref="IZoneUpdateService" /> and a temporary SQLite database. A recording
///     <see cref="ICloudZoneSyncTrigger" /> shows when a push is requested.
/// </summary>
public sealed class CloudPrimaryDynamicUpdateTests : IAsyncLifetime
{
    private const string UpdateKey = "update-key";
    private const string CloudZone = "cloud-primary.example";
    private const string CloudZoneNoSoa = "cloud-no-soa.example";
    private const string CloudZoneDisabled = "cloud-off.example";
    private const string PrimaryZone = "plain-primary.example";
    private const string PrimaryZoneNoSoa = "primary-no-soa.example";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cloud-update-tests-{Guid.NewGuid()}.sqlite3"
    );

    private readonly RecordingTrigger _trigger = new();
    private readonly RecordingLogger _logger = new();
    private readonly Dictionary<string, Guid> _zoneIds = [];

    private IHost _host;
    private AuthoritativeZoneCache _zoneCache;
    private DynamicUpdateQueryResolver _resolver;

    /// <summary>
    ///     This fake records every zone a push was requested for, in call order.
    /// </summary>
    private sealed class RecordingTrigger : ICloudZoneSyncTrigger
    {
        /// <summary>This property lists the zone ids passed to <see cref="RequestSync" />.</summary>
        public List<Guid> Requested { get; } = [];

        /// <inheritdoc />
        public Task SyncZoneNowAsync(Guid zoneId, CancellationToken stoppingToken) =>
            Task.CompletedTask;

        /// <inheritdoc />
        public void RequestSync(Guid zoneId)
        {
            lock (Requested)
                Requested.Add(zoneId);
        }
    }

    /// <summary>
    ///     This logger keeps the level and text of every message, so a test can count the warnings.
    /// </summary>
    private sealed class RecordingLogger : ILogger<DynamicUpdateQueryResolver>
    {
        /// <summary>This property lists the messages logged, with their level.</summary>
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

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
            lock (Entries)
                Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();
        _host = builder.Build();

        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<NinlilDnsContext>()
                .Database.EnsureCreatedAsync();
            IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
            IRecordRepository records =
                scope.ServiceProvider.GetRequiredService<IRecordRepository>();

            foreach (
                (string name, ZoneTypeEnum type, bool enabled, bool withSoa) in new[]
                {
                    (CloudZone, ZoneTypeEnum.CloudPrimary, true, true),
                    (CloudZoneNoSoa, ZoneTypeEnum.CloudPrimary, true, false),
                    (CloudZoneDisabled, ZoneTypeEnum.CloudPrimary, false, true),
                    (PrimaryZone, ZoneTypeEnum.Primary, true, true),
                    (PrimaryZoneNoSoa, ZoneTypeEnum.Primary, true, false),
                }
            )
            {
                Guid zoneId = Guid.NewGuid();
                _zoneIds[name] = zoneId;
                await zones.AddAsync(
                    new ZoneEntity
                    {
                        Id = zoneId,
                        Name = name,
                        Type = type,
                        DynamicUpdateEnabled = enabled,
                        UpdateTsigKeyNames = [UpdateKey],
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );

                if (withSoa)
                    await records.AddAsync(
                        Stored(
                            zoneId,
                            name,
                            DnsResourceRecordTypeEnum.Soa,
                            new SoaRecordData(
                                DnsName.Parse($"ns1.{name}"),
                                DnsName.Parse($"admin.{name}"),
                                100,
                                3600,
                                900,
                                604800,
                                3600
                            )
                        )
                    );
            }

            // A PTR record the delete tests can remove.
            await records.AddAsync(
                Stored(
                    _zoneIds[CloudZone],
                    $"old.{CloudZone}",
                    DnsResourceRecordTypeEnum.Ptr,
                    new PtrRecordData(DnsName.Parse("target.example."))
                )
            );
            await records.AddAsync(
                Stored(
                    _zoneIds[PrimaryZone],
                    PrimaryZone,
                    DnsResourceRecordTypeEnum.NS,
                    new NsRecordData(DnsName.Parse($"ns1.{PrimaryZone}"))
                )
            );
            await records.AddAsync(
                Stored(
                    _zoneIds[CloudZone],
                    CloudZone,
                    DnsResourceRecordTypeEnum.NS,
                    new NsRecordData(DnsName.Parse($"ns1.{CloudZone}"))
                )
            );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        _zoneCache = new AuthoritativeZoneCache(
            _host.Services.GetRequiredService<IServiceScopeFactory>()
        );
        await _zoneCache.StartAsync(CancellationToken.None);
        _resolver = new DynamicUpdateQueryResolver(
            _zoneCache,
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            _trigger,
            _logger
        );
    }

    public async Task DisposeAsync()
    {
        await _zoneCache.StopAsync(CancellationToken.None);
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static RecordEntity Stored(
        Guid zoneId,
        string name,
        DnsResourceRecordTypeEnum type,
        IDnsRecordData data
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = name,
            Type = type,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = 300,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(data.GetType(), data),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static DnsResourceRecord ARecord(string name, byte lastOctet) =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            [192, 0, 2, lastOctet]
        );

    private static DnsResourceRecord PtrRecord(string name) =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.Ptr,
            DnsClassEnum.Internet,
            300,
            DnsName.Parse("host.example.").ToUncompressedWireBytes()
        );

    private static DnsResourceRecord NsRecord(string name, string target) =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            300,
            DnsName.Parse(target).ToUncompressedWireBytes()
        );

    /// <summary>
    ///     This method builds the RFC 2136 §2.5.2 "delete an RRset" form: class ANY, TTL 0, no
    ///     RDATA.
    /// </summary>
    private static DnsResourceRecord DeleteRrset(string name, DnsResourceRecordTypeEnum type) =>
        new(DnsName.Parse(name), type, DnsClassEnum.Any, 0, []);

    /// <summary>
    ///     This method sends an update signed (as far as the resolver can tell) with a verified
    ///     key and returns the response code.
    /// </summary>
    private async Task<DnsResponseCodeEnum> UpdateAsync(
        string zoneName,
        string keyName,
        params DnsResourceRecord[] updates
    )
    {
        DnsMessage request = new()
        {
            Id = 9,
            Opcode = DnsOpcodeEnum.Update,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(zoneName),
                    DnsResourceRecordTypeEnum.Soa,
                    DnsClassEnum.Internet
                ),
            },
        };
        request.AuthorityRecords.AddRange(updates);
        DnsTsigOptions tsig = new(
            DnsName.Parse(keyName),
            DnsTsigOptions.HmacSha256AlgorithmName,
            (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            300,
            [1],
            request.Id,
            DnsResponseCodeEnum.NoError,
            []
        );
        request.Tsig = tsig;

        DnsQueryResolution resolution = await _resolver.TryResolveAsync(
            new DnsQueryContext(
                request,
                DnsTransportProtocolEnum.Udp,
                IPAddress.Loopback,
                DateTimeOffset.UtcNow
            )
            {
                TransactionSignature = new DnsTransactionSignature(
                    keyName,
                    true,
                    DnsResponseCodeEnum.NoError,
                    tsig
                ),
            }
        );

        Assert.NotNull(resolution.Response);
        return resolution.Response.ResponseCode;
    }

    private async Task<IReadOnlyList<RecordEntity>> RecordsOfAsync(string zoneName)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .ListByZoneIdAsync(_zoneIds[zoneName]);
    }

    private async Task<uint> SerialOfAsync(string zoneName)
    {
        RecordEntity soa = (await RecordsOfAsync(zoneName)).Single(record =>
            record.Type == DnsResourceRecordTypeEnum.Soa
        );
        return Ninlil
            .Common.Serializer.JsonSerializer.Deserialize<SoaRecordData>(soa.DataJson)
            .Serial;
    }

    [Fact]
    public async Task Update_AddingAnARecordToACloudPrimaryZone_IsAppliedAndRequestsOnePush()
    {
        uint serialBefore = await SerialOfAsync(CloudZone);

        DnsResponseCodeEnum code = await UpdateAsync(
            CloudZone,
            UpdateKey,
            ARecord($"www.{CloudZone}", 10)
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, code);
        Assert.Contains(
            await RecordsOfAsync(CloudZone),
            record =>
                record.Type == DnsResourceRecordTypeEnum.A
                && record.Name.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Equal(serialBefore + 1, await SerialOfAsync(CloudZone));
        Assert.Equal([_zoneIds[CloudZone]], _trigger.Requested);
        Assert.Single(
            _logger.Entries,
            entry => entry.Level == LogLevel.Information && entry.Message.Contains($"'{CloudZone}'")
        );
    }

    [Fact]
    public async Task Update_AddingAPtrRecordToACloudPrimaryZone_IsRefusedAndStoresNothing()
    {
        uint serialBefore = await SerialOfAsync(CloudZone);

        DnsResponseCodeEnum code = await UpdateAsync(
            CloudZone,
            UpdateKey,
            PtrRecord($"new-ptr.{CloudZone}")
        );

        Assert.Equal(DnsResponseCodeEnum.Refused, code);
        Assert.DoesNotContain(
            await RecordsOfAsync(CloudZone),
            record => record.Name.StartsWith("new-ptr.", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Equal(serialBefore, await SerialOfAsync(CloudZone));
        Assert.Empty(_trigger.Requested);
    }

    [Fact]
    public async Task Update_AddingAnARecordAndAPtrRecordTogether_AppliesNeitherOfThem()
    {
        // Control: the A record alone is accepted by the same zone (see the first test), so the
        // refusal here comes from the PTR record, and the whole update is atomic (RFC 2136 §3.4.2).
        DnsResponseCodeEnum code = await UpdateAsync(
            CloudZone,
            UpdateKey,
            ARecord($"together.{CloudZone}", 11),
            PtrRecord($"together-ptr.{CloudZone}")
        );

        Assert.Equal(DnsResponseCodeEnum.Refused, code);
        Assert.DoesNotContain(
            await RecordsOfAsync(CloudZone),
            record => record.Name.StartsWith("together", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Empty(_trigger.Requested);
    }

    [Fact]
    public async Task Update_DeletingAPtrRrsetFromACloudPrimaryZone_IsAppliedAndRequestsAPush()
    {
        DnsResponseCodeEnum code = await UpdateAsync(
            CloudZone,
            UpdateKey,
            DeleteRrset($"old.{CloudZone}", DnsResourceRecordTypeEnum.Ptr)
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, code);
        Assert.DoesNotContain(
            await RecordsOfAsync(CloudZone),
            record => record.Type == DnsResourceRecordTypeEnum.Ptr
        );
        Assert.Equal([_zoneIds[CloudZone]], _trigger.Requested);
    }

    [Fact]
    public async Task Update_AddingAnApexNsRecordToACloudPrimaryZone_IsAppliedAsLocalOnlyData()
    {
        DnsResponseCodeEnum code = await UpdateAsync(
            CloudZone,
            UpdateKey,
            NsRecord(CloudZone, $"ns2.{CloudZone}.")
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, code);
        Assert.Equal(
            2,
            (await RecordsOfAsync(CloudZone)).Count(record =>
                record.Type == DnsResourceRecordTypeEnum.NS
            )
        );
    }

    [Fact]
    public async Task Update_AddingAPtrRecordToAPrimaryZone_IsAppliedAndRequestsNoPush()
    {
        DnsResponseCodeEnum code = await UpdateAsync(
            PrimaryZone,
            UpdateKey,
            PtrRecord($"ptr.{PrimaryZone}")
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, code);
        Assert.Contains(
            await RecordsOfAsync(PrimaryZone),
            record => record.Type == DnsResourceRecordTypeEnum.Ptr
        );
        Assert.Empty(_trigger.Requested);
    }

    [Fact]
    public async Task Update_ThatChangesNothingInACloudPrimaryZone_RequestsNoPush()
    {
        DnsResponseCodeEnum code = await UpdateAsync(
            CloudZone,
            UpdateKey,
            DeleteRrset($"absent.{CloudZone}", DnsResourceRecordTypeEnum.A)
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, code);
        Assert.Empty(_trigger.Requested);
    }

    [Fact]
    public async Task Update_ToACloudPrimaryZoneWithUpdatesDisabled_IsRefused()
    {
        Assert.Equal(
            DnsResponseCodeEnum.Refused,
            await UpdateAsync(CloudZoneDisabled, UpdateKey, ARecord($"x.{CloudZoneDisabled}", 12))
        );
        Assert.Empty(_trigger.Requested);
    }

    [Fact]
    public async Task Update_ToACloudPrimaryZoneWithAKeyNotBoundToIt_IsRefused()
    {
        Assert.Equal(
            DnsResponseCodeEnum.Refused,
            await UpdateAsync(CloudZone, "other-key", ARecord($"y.{CloudZone}", 13))
        );
        Assert.Empty(_trigger.Requested);
    }

    [Theory]
    [InlineData(PrimaryZoneNoSoa)]
    [InlineData(CloudZoneNoSoa)]
    public async Task Update_ToAZoneWithoutAnSoa_GetsServerFailureAndOneWarning(string zoneName)
    {
        DnsResponseCodeEnum code = await UpdateAsync(
            zoneName,
            UpdateKey,
            ARecord($"www.{zoneName}", 14)
        );

        Assert.Equal(DnsResponseCodeEnum.ServerFailure, code);
        Assert.Empty(await RecordsOfAsync(zoneName));
        Assert.Single(
            _logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains($"'{zoneName}'")
        );
        Assert.Empty(_trigger.Requested);
    }

    [Theory]
    [InlineData(DnsResourceRecordTypeEnum.A, "www.zone.example", true)]
    [InlineData(DnsResourceRecordTypeEnum.Aaaa, "www.zone.example", true)]
    [InlineData(DnsResourceRecordTypeEnum.Cname, "www.zone.example", true)]
    [InlineData(DnsResourceRecordTypeEnum.MX, "zone.example", true)]
    [InlineData(DnsResourceRecordTypeEnum.Txt, "zone.example", true)]
    [InlineData(DnsResourceRecordTypeEnum.Srv, "_sip._tcp.zone.example", true)]
    [InlineData(DnsResourceRecordTypeEnum.Caa, "zone.example", true)]
    [InlineData(DnsResourceRecordTypeEnum.NS, "sub.zone.example", true)]
    [InlineData(DnsResourceRecordTypeEnum.Ptr, "1.zone.example", false)]
    [InlineData(DnsResourceRecordTypeEnum.Naptr, "zone.example", false)]
    [InlineData(DnsResourceRecordTypeEnum.Tlsa, "_443._tcp.zone.example", false)]
    [InlineData(DnsResourceRecordTypeEnum.Soa, "zone.example", true)]
    [InlineData(DnsResourceRecordTypeEnum.Soa, "ZONE.example.", true)]
    [InlineData(DnsResourceRecordTypeEnum.NS, "zone.example", true)]
    public void IsPushable_ForAType_FollowsTheProviderTypesAndTheApexExceptions(
        DnsResourceRecordTypeEnum type,
        string name,
        bool expected
    )
    {
        RecordEntity record = new() { Type = type, Name = name };

        Assert.Equal(expected, CloudZoneUpdatePolicy.IsPushable(record, "zone.example"));
    }

    [Fact]
    public void IsPushable_ForANonApexSoa_IsFalse()
    {
        RecordEntity record = new()
        {
            Type = DnsResourceRecordTypeEnum.Soa,
            Name = "sub.zone.example",
        };

        Assert.False(CloudZoneUpdatePolicy.IsPushable(record, "zone.example"));
    }
}
