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
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Recursive.Validation;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class tests the NSEC3 Opt-Out flag set by <see cref="ZoneSigningService" />
///     (<c>BuildNsec3Chain</c>, RFC 5155 §7.1): a zone with an insecure delegation has the flag on
///     every NSEC3 record, and a zone without one has it clear. Both cases are also checked with
///     <see cref="DenialOfExistenceValidator.VerifyNoDataNsec3" />, which the recursive resolver
///     uses for the missing-DS proof.
/// </summary>
public sealed class ZoneSigningServiceNsec3OptOutTests : IAsyncLifetime
{
    private const string ZoneName = "nsec3-optout.example.com";
    private const string InsecureDelegationName = "insecure-child.nsec3-optout.example.com";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-nsec3-optout-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private Guid _zoneId;

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

        _zoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = ZoneName,
                Type = ZoneTypeEnum.Primary,
                IsDnssecSigned = true,
                DnssecAlgorithm = DnssecAlgorithmEnum.Ed25519,
                DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec3,
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
                        1,
                        3600,
                        900,
                        604800,
                        3600
                    )
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        // An ordinary signed record in both zones, so the insecure delegation is the only
        // difference between them.
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
                    new ARecordData(System.Net.IPAddress.Parse("192.0.2.20"))
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        // An insecure delegation: NS at a non-apex name with no local signed child, so
        // ComputeDelegationDsRecordsAsync generates no DS for it, which turns on Opt-Out.
        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Name = InsecureDelegationName,
                Type = DnsResourceRecordTypeEnum.NS,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new NsRecordData(DnsName.Parse($"ns1.{InsecureDelegationName}"))
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
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    [Fact]
    public async Task AZoneWithARealInsecureDelegationSetsOptOutOnEveryNsec3Record()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneSigningService signingService =
            scope.ServiceProvider.GetRequiredService<IZoneSigningService>();

        SignedZoneData signed = await signingService.SignZoneAsync(_zoneId);

        List<(DnsName Owner, Nsec3RecordData Data)> nsec3Records =
        [
            .. signed
                .DenialOfExistenceRecords.Where(record =>
                    record.Type == DnsResourceRecordTypeEnum.Nsec3
                )
                .Select(record =>
                    (
                        record.Name,
                        (Nsec3RecordData)
                            DnsRecordDataCodecRegistry.Decode(
                                DnsResourceRecordTypeEnum.Nsec3,
                                record.Data
                            )
                    )
                ),
        ];

        // The chain covers at least the apex, "www" and the insecure delegation.
        Assert.True(nsec3Records.Count >= 3);

        // The zone has an insecure delegation, so the signer sets Opt-Out on every record
        // (RFC 5155 §3.1.2.1).
        Assert.All(nsec3Records, entry => Assert.True(entry.Data.IsOptOut));

        // Validator, case 1: the signer keeps the insecure delegation's NSEC3 record (RFC 5155 §7.1
        // allows omitting it), so the delegation is proven insecure by the missing DS bit in that
        // record (RFC 5155 §6), through the matching-record branch of VerifyNoDataNsec3.
        Assert.Equal(
            DenialOfExistenceOutcomeEnum.Proven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse(InsecureDelegationName),
                DnsResourceRecordTypeEnum.DS,
                DnsName.Parse(ZoneName),
                signed.DenialOfExistenceRecords
            )
        );

        // Validator, case 2: had the signer omitted the delegation's record (the other case in RFC
        // 5155 §6), the Opt-Out flag on the covering record must still let a validator accept the
        // delegation as possibly insecure (the closest provable encloser is the apex). Simulate the omission by removing the record and pointing
        // its predecessor's next hash at its successor, as a signer omitting it would have done.
        int delegationIndex = nsec3Records.FindIndex(entry =>
            Nsec3Hasher
                .ExtractHashFromOwnerName(entry.Owner)
                .AsSpan()
                .SequenceEqual(
                    Nsec3Hasher.ComputeHash(
                        entry.Data.HashAlgorithm,
                        DnsName.Parse(InsecureDelegationName),
                        entry.Data.Iterations,
                        entry.Data.Salt
                    )
                )
        );
        Assert.True(delegationIndex >= 0);

        List<DnsResourceRecord> orderedNsec3RawRecords =
        [
            .. signed.DenialOfExistenceRecords.Where(record =>
                record.Type == DnsResourceRecordTypeEnum.Nsec3
            ),
        ];
        int predecessorIndex =
            (delegationIndex - 1 + orderedNsec3RawRecords.Count) % orderedNsec3RawRecords.Count;
        int successorIndex = (delegationIndex + 1) % orderedNsec3RawRecords.Count;

        DnsResourceRecord predecessorRecord = orderedNsec3RawRecords[predecessorIndex];
        Nsec3RecordData predecessorData = nsec3Records[predecessorIndex].Data;
        byte[] successorHash = Nsec3Hasher.ExtractHashFromOwnerName(
            orderedNsec3RawRecords[successorIndex].Name
        );

        DnsResourceRecord rewiredPredecessorRecord = new(
            predecessorRecord.Name,
            predecessorRecord.Type,
            predecessorRecord.Class,
            predecessorRecord.TimeToLive,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Nsec3,
                new Nsec3RecordData(
                    predecessorData.HashAlgorithm,
                    predecessorData.IsOptOut,
                    predecessorData.Iterations,
                    predecessorData.Salt,
                    successorHash,
                    predecessorData.Types
                )
            )
        );

        List<DnsResourceRecord> chainWithDelegationRecordOmitted =
        [
            .. signed.DenialOfExistenceRecords.Where(record =>
                record.Type != DnsResourceRecordTypeEnum.Nsec3
                || (
                    record != orderedNsec3RawRecords[delegationIndex] && record != predecessorRecord
                )
            ),
            rewiredPredecessorRecord,
        ];

        Assert.Equal(
            DenialOfExistenceOutcomeEnum.ProvenInsecure,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse(InsecureDelegationName),
                DnsResourceRecordTypeEnum.DS,
                DnsName.Parse(ZoneName),
                chainWithDelegationRecordOmitted
            )
        );
    }

    [Fact]
    public async Task AZoneWithNoInsecureDelegationLeavesOptOutClearOnEveryNsec3Record()
    {
        // A zone with no delegation: Opt-Out does not apply and every flag stays clear.
        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        IZoneRepository zoneRepository =
            seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            seedScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDnssecKeyManagementService keyManagement =
            seedScope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();

        const string zoneName = "nsec3-no-delegation.example.com";
        Guid zoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = zoneName,
                Type = ZoneTypeEnum.Primary,
                IsDnssecSigned = true,
                DnssecAlgorithm = DnssecAlgorithmEnum.Ed25519,
                DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec3,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = zoneId,
                Name = zoneName,
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{zoneName}"),
                        DnsName.Parse($"admin.{zoneName}"),
                        1,
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
                ZoneId = zoneId,
                Name = $"www.{zoneName}",
                Type = DnsResourceRecordTypeEnum.A,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 300,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new ARecordData(System.Net.IPAddress.Parse("192.0.2.30"))
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await keyManagement.GenerateKeyAsync(
            zoneId,
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.Ed25519
        );
        await keyManagement.GenerateKeyAsync(
            zoneId,
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneSigningService signingService =
            scope.ServiceProvider.GetRequiredService<IZoneSigningService>();

        SignedZoneData signed = await signingService.SignZoneAsync(zoneId);

        List<Nsec3RecordData> nsec3Records =
        [
            .. signed
                .DenialOfExistenceRecords.Where(record =>
                    record.Type == DnsResourceRecordTypeEnum.Nsec3
                )
                .Select(record =>
                    (Nsec3RecordData)
                        DnsRecordDataCodecRegistry.Decode(
                            DnsResourceRecordTypeEnum.Nsec3,
                            record.Data
                        )
                ),
        ];

        Assert.NotEmpty(nsec3Records);
        Assert.All(nsec3Records, data => Assert.False(data.IsOptOut));
    }
}
