using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Resolution;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Dns.Server.Tsig.Extension;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class checks that the DNSSEC records a <see cref="ZoneTypeEnum.Secondary" /> zone
///     receives by AXFR reach <see cref="SignedZoneCache" />, so
///     <see cref="AuthoritativeQueryResolver" /> returns RRSIGs to DNSSEC OK queries at the
///     secondary. It uses two hosts, each with its own SQLite database, as primary and secondary.
///     <para>
///         The primary's <see cref="ZoneTransferService" /> builds a TSIG-signed AXFR of a zone
///         signed by <see cref="IZoneSigningService" /> with newly generated keys, and the records are
///         written to the secondary with <see cref="IZoneUpdateService.BootstrapZoneAsync" />, the
///         call <see cref="Refresh.SecondaryZoneRefreshService" /> makes on a first pull, so the real
///         cache update path runs.
///     </para>
/// </summary>
public sealed class SecondaryZoneDnssecTransferCacheTests : IAsyncLifetime
{
    private const string ZoneName = "secondary-signed16b.example.com";

    private readonly string _primaryDatabasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-16b-primary-{Guid.NewGuid()}.sqlite3"
    );

    private readonly string _secondaryDatabasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-16b-secondary-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _primaryHost;
    private IHost _secondaryHost;
    private Guid _zoneId;
    private string _keyName;

    public async Task InitializeAsync()
    {
        _zoneId = Guid.NewGuid();
        _keyName = $"transfer-16b-key-{Guid.NewGuid()}";

        _primaryHost = BuildHost(_primaryDatabasePath);
        _secondaryHost = BuildHost(_secondaryDatabasePath);

        await SeedPrimaryAsync();
        await SeedSecondaryZoneOnlyAsync();

        // SignedZoneCache.StartAsync (an IHostedService) runs its initial full sign
        // synchronously, so the primary is already signed and ready to serve a real AXFR by the
        // time this returns -- mirroring ZoneTransferServiceDnssecTests' setup.
        await _primaryHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _primaryHost.StopAsync();
        _primaryHost.Dispose();

        if (_secondaryHostStarted)
            await _secondaryHost.StopAsync();

        _secondaryHost.Dispose();

        if (File.Exists(_primaryDatabasePath))
            File.Delete(_primaryDatabasePath);

        if (File.Exists(_secondaryDatabasePath))
            File.Delete(_secondaryDatabasePath);
    }

    private bool _secondaryHostStarted;

    /// <summary>
    ///     This test covers the "already on disk at boot" path (<see cref="SignedZoneCache.StartAsync" />):
    ///     the secondary's database already holds a prior AXFR's transferred signed content
    ///     before the process (here, <see cref="_secondaryHost" />) ever starts, the shape
    ///     a real restart of an already-synced secondary would see.
    /// </summary>
    [Fact]
    public async Task ASecondaryZonesAlreadyTransferredContentIsLoadedAtStartupAndServedWithARrsig()
    {
        List<DnsResourceRecord> transferredRecords = await PullSignedAxfrFromPrimaryAsync();
        await BootstrapSecondaryRecordsAsync(transferredRecords);

        await _secondaryHost.StartAsync();
        _secondaryHostStarted = true;

        SignedZoneCache secondaryCache =
            _secondaryHost.Services.GetRequiredService<SignedZoneCache>();
        SignedZoneData signedData = secondaryCache.GetSignedData(_zoneId);

        Assert.NotNull(signedData);
        Assert.NotEmpty(signedData.DnsKeyRecords);

        await AssertResolverServesTheRrsigAsync();
    }

    /// <summary>
    ///     This test covers the live, event-driven path: the secondary starts with the zone
    ///     already configured as signed but empty (its very first transfer has not
    ///     completed yet), and only picks up the transferred content afterward -- through the
    ///     real, unmodified <c>DnsZoneChangedEvent</c> pipeline
    ///     (<see cref="IZoneUpdateService.BootstrapZoneAsync" /> publishes it,
    ///     <see cref="SignedZoneChangedEventSubscriber" /> reacts to it), not a direct method call
    ///     -- proving the actual production hook, delivered asynchronously as a real
    ///     transfer completing while the process is already running would deliver it.
    /// </summary>
    [Fact]
    public async Task ATransferCompletingAfterStartupAsynchronouslyPopulatesTheCacheAndServesARrsig()
    {
        await _secondaryHost.StartAsync();
        _secondaryHostStarted = true;

        SignedZoneCache secondaryCache =
            _secondaryHost.Services.GetRequiredService<SignedZoneCache>();

        // Nothing has been transferred yet -- a signed-but-empty secondary must not appear in
        // this cache at all.
        Assert.Null(secondaryCache.GetSignedData(_zoneId));

        List<DnsResourceRecord> transferredRecords = await PullSignedAxfrFromPrimaryAsync();
        await BootstrapSecondaryRecordsAsync(transferredRecords);

        // The event this bootstrap just published is delivered off a background channel
        // (DnsServerEventDispatcher/DnsServerEventPublisher), so it is not necessarily visible
        // the instant BootstrapZoneAsync returns -- poll with a bounded retry, the same pattern
        // this project's CloudDnsZoneSyncFailureEventTests already establishes for a
        // asynchronous event delivery.
        SignedZoneData signedData = null;

        for (int attempt = 0; attempt < 100 && signedData is null; attempt++)
        {
            signedData = secondaryCache.GetSignedData(_zoneId);

            if (signedData is null)
                await Task.Delay(100);
        }

        Assert.NotNull(signedData);
        Assert.NotEmpty(signedData.DnsKeyRecords);

        await AssertResolverServesTheRrsigAsync();
    }

    /// <summary>
    ///     This method builds a fully wired host (Core/TSIG/DNSSEC/Authoritative), backed by its
    ///     own dedicated SQLite database -- one call for the primary, one for the secondary,
    ///     standing in for two independent server processes.
    /// </summary>
    private static IHost BuildHost(string databasePath)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);

        return builder.Build();
    }

    /// <summary>
    ///     This method seeds the primary host with a real signed zone: SOA, one ordinary A
    ///     record, a real ZSK/KSK pair, and the TSIG key the AXFR pull below authenticates with.
    /// </summary>
    private async Task SeedPrimaryAsync()
    {
        await using AsyncServiceScope scope = _primaryHost.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDnssecKeyManagementService keyManagement =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();
        ITsigKeyRepository tsigKeyRepository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IDataProtectionProvider dataProtectionProvider =
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = ZoneName,
                Type = ZoneTypeEnum.Primary,
                IsDnssecSigned = true,
                DnssecAlgorithm = DnssecAlgorithmEnum.Ed25519,
                DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec,
                TransferTsigKeyNames = [_keyName],
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Name = ZoneName,
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{ZoneName}"),
                        DnsName.Parse($"admin.{ZoneName}"),
                        100,
                        3600,
                        900,
                        604800,
                        3600
                    )
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Name = $"www.{ZoneName}",
                Type = DnsResourceRecordTypeEnum.A,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 300,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new ARecordData(IPAddress.Parse("192.0.2.20"))
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.Ed25519
        );
        await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );

        IDataProtector protector = dataProtectionProvider.CreateProtector(
            "Ninlil.Dns.Server.Tsig.SharedSecret"
        );
        byte[] rawSecret = new byte[32];
        Random.Shared.NextBytes(rawSecret);

        await tsigKeyRepository.AddAsync(
            new TsigKeyEntity
            {
                Id = Guid.NewGuid(),
                Name = _keyName,
                AlgorithmName = "hmac-sha256.",
                SecretEncrypted = protector.Protect(Convert.ToBase64String(rawSecret)),
                IsDisabled = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This method seeds the secondary host with only its <see cref="ZoneEntity" /> row --
    ///     no records at all yet, matching a real secondary's configuration existing before
    ///     its first successful transfer. Its DNSSEC-relevant settings (<c>IsDnssecSigned</c>,
    ///     <c>DenialOfExistenceMode</c>) mirror the primary's own, the same way an
    ///     operator configuring a real secondary of a signed zone is expected to.
    /// </summary>
    private async Task SeedSecondaryZoneOnlyAsync()
    {
        await using AsyncServiceScope scope = _secondaryHost.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // PrimaryServerAddresses is left empty -- this test drives the "transfer"
        // itself (BootstrapSecondaryRecordsAsync below), so SecondaryZoneRefreshService's own
        // background poll loop (also registered on this host, unconditionally) has nothing to
        // reach and simply logs "no configured primary server addresses" rather than attempting
        // a real, doomed-to-fail network round trip against a TSIG key this database does not
        // hold at all.
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = ZoneName,
                Type = ZoneTypeEnum.Secondary,
                IsDnssecSigned = true,
                DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec,
                TsigKeyName = _keyName,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This method performs a real, TSIG-signed AXFR against the primary host and returns
    ///     every transferred wire record -- the unsigned base content (SOA, A) alongside the real,
    ///     freshly computed DNSKEY/RRSIG/NSEC content <see cref="ZoneTransferService" /> appends
    ///     for a signed zone.
    /// </summary>
    private async Task<List<DnsResourceRecord>> PullSignedAxfrFromPrimaryAsync()
    {
        await using AsyncServiceScope scope = _primaryHost.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();
        IZoneTransferService zoneTransferService =
            scope.ServiceProvider.GetRequiredService<IZoneTransferService>();

        DnsMessage request = new()
        {
            Id = 1,
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

        DnsMessage signed = await tsigService.SignAsync(request, _keyName, request.Id);
        byte[] rawMessage = DnsMessageWriter.Write(signed);
        DnsMessage reparsed = DnsMessageReader.Read(rawMessage, out int tsigOffset);

        DnsQueryContext context = new(
            reparsed,
            DnsTransportProtocolEnum.Tcp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow,
            rawMessage,
            tsigOffset
        );

        List<DnsResourceRecord> allRecords = [];

        await foreach (DnsMessage message in zoneTransferService.CreateAxfrResponseAsync(context))
        {
            Assert.Equal(DnsResponseCodeEnum.NoError, message.ResponseCode);
            allRecords.AddRange(message.Answers);
        }

        // Sanity-check the transfer itself carried signed content, so a failure in the
        // assertions below can never be mistaken for "the primary never signed anything" instead
        // of the real thing this test exists to prove.
        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.DnsKey);
        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.RrSig);

        return allRecords;
    }

    /// <summary>
    ///     This method writes a transfer's records into the secondary's database through
    ///     <see cref="IZoneUpdateService.BootstrapZoneAsync" /> -- the same call
    ///     <see cref="Refresh.SecondaryZoneRefreshService" />'s initial-pull path makes, so
    ///     this test exercises the real production write path rather than a test-only shortcut.
    /// </summary>
    private async Task BootstrapSecondaryRecordsAsync(
        IReadOnlyList<DnsResourceRecord> transferredRecords
    )
    {
        await using AsyncServiceScope scope = _secondaryHost.Services.CreateAsyncScope();
        IZoneUpdateService zoneUpdateService =
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();

        List<RecordEntity> secondaryRecords =
        [
            .. transferredRecords
                .Select(record => RecordEntityCodec.FromWireRecord(record, _zoneId))
                .Where(record => record is not null),
        ];

        await zoneUpdateService.BootstrapZoneAsync(_zoneId, secondaryRecords);
    }

    /// <summary>
    ///     This method sends a DNSSEC OK query for the transferred A record to the secondary's
    ///     <see cref="AuthoritativeQueryResolver" /> and asserts the answer includes its RRSIG.
    /// </summary>
    private async Task AssertResolverServesTheRrsigAsync()
    {
        AuthoritativeQueryResolver resolver = _secondaryHost
            .Services.GetServices<IDnsQueryResolver>()
            .OfType<AuthoritativeQueryResolver>()
            .Single();

        DnsMessage request = new()
        {
            Id = 42,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = false,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse($"www.{ZoneName}"),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
            Edns = new DnsEdnsOptions(0, 4096, true, []),
        };

        DnsQueryContext context = new(
            request,
            DnsTransportProtocolEnum.Udp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(context);

        Assert.True(resolution.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NoError, resolution.Response.ResponseCode);
        Assert.Contains(
            resolution.Response.Answers,
            record => record.Type == DnsResourceRecordTypeEnum.A
        );

        // The RRSIG must be attached, which needs SignedZoneCache to hold the secondary zone's
        // transferred signed data.
        DnsResourceRecord rrsig = Assert.Single(
            resolution.Response.Answers,
            record => record.Type == DnsResourceRecordTypeEnum.RrSig
        );
        RrSigRecordData rrsigData = Assert.IsType<RrSigRecordData>(
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, rrsig.Data)
        );
        Assert.Equal(DnsResourceRecordTypeEnum.A, rrsigData.TypeCovered);
    }
}
