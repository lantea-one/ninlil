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
///     This class covers <see cref="ZoneSigningService" />'s own private
///     <c>BuildNsec3Chain</c> method and its RFC 5155 §6/§7.1 Opt-Out handling. Before this fix,
///     every generated <see cref="Nsec3RecordData" />'s own <see cref="Nsec3RecordData.IsOptOut" />
///     flag was hardcoded <see langword="false" />, regardless of whether the zone actually
///     contained an insecure (non-DS) delegation -- this proves the real fix both ways: a zone
///     with a genuine insecure delegation gets the flag set on every NSEC3 record it generates
///     (RFC 5155 §7.1 step 2's own "if Opt-Out is being used, set the Opt-Out bit to one" applies
///     zone-wide, not per-record), and a zone with none at all keeps every flag clear. Both cases
///     are also proven from the *validator* side, via the real
///     <see cref="DenialOfExistenceValidator.VerifyOptOut" /> this project's own recursive resolver
///     actually calls -- confirming the fix is not merely "the bit looks right," but that a real
///     validator genuinely accepts it as proof an unproven insecure delegation may exist.
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

        // An ordinary, fully-signed record -- present in both this test's zones, so a genuinely
        // insecure delegation elsewhere is the only real variable between them.
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

        // A real insecure delegation: an NS record at a non-apex name with no locally hosted,
        // DNSSEC-signed child zone behind it, so ComputeDelegationDsRecordsAsync generates no DS
        // for it at all -- exactly the condition this fix keys off.
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

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
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

        // A real NSEC3 chain: the apex, "www", and the insecure delegation itself all need their
        // own record.
        Assert.True(nsec3Records.Count >= 3);

        // Before the fix, every one of these was hardcoded false -- now every single one must be
        // set, since this zone genuinely has an insecure delegation and RFC 5155 §7.1 step 2's
        // own construction algorithm sets the bit zone-wide once Opt-Out applies at all.
        Assert.All(nsec3Records, entry => Assert.True(entry.Data.IsOptOut));

        // The real validator side, case 1: this zone's own signing pass never actually omits an
        // insecure delegation's own NSEC3 record (RFC 5155 §7.1 step 2 makes that omission
        // optional, "MAY be excluded" -- this project always includes it), so the delegation's own
        // insecurity is genuinely proven the way RFC 5155 §6's own third paragraph describes for
        // exactly this case: "the delegation is proven insecure by the lack of a DS bit in the
        // type map" of its own, explicitly present, NSEC3 record -- VerifyNoDataNsec3, the same
        // method DnssecValidator.VerifyAbsenceOfDs calls first, before ever falling back to
        // VerifyOptOut.
        Assert.True(
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse(InsecureDelegationName),
                DnsResourceRecordTypeEnum.DS,
                signed.DenialOfExistenceRecords
            )
        );

        // The real validator side, case 2: proves the Opt-Out flag this fix now sets is not just
        // present but genuinely interoperable -- if this zone's signing pass *had* chosen to omit
        // the delegation's own explicit record (RFC 5155 §6's other, more common real-world case,
        // this project's own genuine signing choice not to do so notwithstanding), the very same
        // NSEC3 chain this pass actually produced would still let a real validator prove the
        // delegation may be insecure, purely from its neighboring Opt-Out record's own covering
        // range. Simulated here by actually removing the delegation's own record and rewiring its
        // predecessor's own NextHashedOwnerName to skip directly to its successor -- exactly what
        // a signing pass that chose to omit it would itself have produced -- rather than just
        // deleting the entry and leaving a dangling pointer, which would prove nothing real.
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

        Assert.True(
            DenialOfExistenceValidator.VerifyOptOut(
                DnsName.Parse(InsecureDelegationName),
                chainWithDelegationRecordOmitted
            )
        );
    }

    [Fact]
    public async Task AZoneWithNoInsecureDelegationLeavesOptOutClearOnEveryNsec3Record()
    {
        // A second zone, this time with no delegation at all -- the fix's own "else" branch: no
        // insecure delegation exists, so Opt-Out never applies and every flag stays clear.
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
