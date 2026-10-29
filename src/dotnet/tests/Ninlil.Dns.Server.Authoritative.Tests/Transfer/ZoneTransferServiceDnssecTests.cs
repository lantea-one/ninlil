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
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Dns.Server.Tsig.Extension;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests how <see cref="ZoneTransferService" /> handles a signed zone, on a host with a
///     temporary SQLite database and a generated key: the DNSKEY, RRSIG and NSEC records appear in
///     AXFR and IXFR answers, and IXFR for a signed zone is always a full transfer (see
///     <see cref="ZoneTransferService.CreateIxfrResponseAsync" />).
/// </summary>
public sealed class ZoneTransferServiceDnssecTests : IAsyncLifetime
{
    private const string ZoneName = "signed-transfer.example.com";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-zonetransfer-dnssec-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private Guid _zoneId;
    private string _keyName;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            seedScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDnssecKeyManagementService keyManagement =
            seedScope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();
        ITsigKeyRepository tsigKeyRepository =
            seedScope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IDataProtectionProvider dataProtectionProvider =
            seedScope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

        _zoneId = Guid.NewGuid();
        _keyName = $"transfer-test-key-{Guid.NewGuid()}";

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
                    new ARecordData(IPAddress.Parse("192.0.2.10"))
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        // A real, freshly generated key of each role -- SignedZoneCache's StartAsync
        // (triggered by _host.StartAsync() below) needs at least one to produce a
        // signed result rather than logging a failure and leaving this zone out of its cache.
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

        // A real TSIG key -- ZoneTransferService.AuthorizeAsync always requires one, regardless
        // of this zone's configuration.
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

        // Runs AuthoritativeZoneCache's and SignedZoneCache's initial full load/sign
        // synchronously, so both are already warm by the time this method returns.
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    /// <summary>
    ///     This method builds a real, TSIG-signed transfer request and re-parses it back into
    ///     wire form -- mirroring what a real transport does, so the TSIG record's starting
    ///     offset is known the same way <see cref="ZoneTransferService" />'s request
    ///     authorization check expects.
    /// </summary>
    private async Task<DnsQueryContext> BuildSignedRequestAsync(
        DnsQuestion question,
        IReadOnlyList<DnsResourceRecord> authorityRecords = null
    )
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        DnsMessage request = new()
        {
            Id = 1,
            Opcode = DnsOpcodeEnum.Query,
            Questions = { question },
        };

        if (authorityRecords is not null)
            request.AuthorityRecords.AddRange(authorityRecords);

        DnsMessage signed = await tsigService.SignAsync(request, _keyName, request.Id);
        byte[] rawMessage = DnsMessageWriter.Write(signed);
        DnsMessage reparsed = DnsMessageReader.Read(rawMessage, out int tsigOffset);

        return new DnsQueryContext(
            reparsed,
            DnsTransportProtocolEnum.Tcp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow,
            rawMessage,
            tsigOffset
        );
    }

    [Fact]
    public async Task AxfrIncludesTheZonesSignedContent()
    {
        DnsQueryContext context = await BuildSignedRequestAsync(
            new DnsQuestion(
                DnsName.Parse(ZoneName),
                DnsResourceRecordTypeEnum.Axfr,
                DnsClassEnum.Internet
            )
        );

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneTransferService zoneTransferService =
            scope.ServiceProvider.GetRequiredService<IZoneTransferService>();

        List<DnsResourceRecord> allRecords = [];

        await foreach (DnsMessage message in zoneTransferService.CreateAxfrResponseAsync(context))
        {
            Assert.Equal(DnsResponseCodeEnum.NoError, message.ResponseCode);
            allRecords.AddRange(message.Answers);
        }

        // The unsigned base content is still there...
        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.A);
        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.Soa);

        // ...with every kind of signed record the signing pass produces.
        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.DnsKey);
        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.RrSig);
        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.Nsec);
    }

    [Fact]
    public async Task IxfrForASignedZoneAlwaysServesAFullTransferIncludingSignedContent()
    {
        // The client claims a much older serial than the zone's real current one (100) -- a
        // range an unsigned zone's journal history would ordinarily be eligible to replay
        // incrementally, used here specifically to prove a signed zone never takes that path.
        DnsResourceRecord clientSoa = new(
            DnsName.Parse(ZoneName),
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(DnsName.Root, DnsName.Root, 50, 0, 0, 0, 0)
            )
        );

        DnsQueryContext context = await BuildSignedRequestAsync(
            new DnsQuestion(
                DnsName.Parse(ZoneName),
                DnsResourceRecordTypeEnum.Ixfr,
                DnsClassEnum.Internet
            ),
            [clientSoa]
        );

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneTransferService zoneTransferService =
            scope.ServiceProvider.GetRequiredService<IZoneTransferService>();

        List<DnsResourceRecord> allRecords = [];

        await foreach (DnsMessage message in zoneTransferService.CreateIxfrResponseAsync(context))
            allRecords.AddRange(message.Answers);

        // A true incremental diff would show a second, older-serial SOA immediately after the
        // leading current-serial one; a full transfer instead goes straight into ordinary
        // content, the same "AXFR in IXFR's clothing" shape RFC 1995 §4 already permits.
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, allRecords[0].Type);
        Assert.NotEqual(DnsResourceRecordTypeEnum.Soa, allRecords[1].Type);

        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.A);
        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.DnsKey);
        Assert.Contains(allRecords, record => record.Type == DnsResourceRecordTypeEnum.RrSig);
    }
}
