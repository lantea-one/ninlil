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
///     This class tests that a signed zone with mixed-case names validates with the recursive
///     resolver's <see cref="RrsetSignatureVerifier" />: the zone name, owner names and the NSEC
///     Next Domain Names keep their stored case, and the signer lower-cases the Signer's Name and
///     the owner in the signing input while leaving NSEC RDATA untouched (RFC 4034 §6.2, RFC 6840
///     §5.1). A wildcard record is also verified when presented as an expansion (RFC 4035 §5.3.2).
///     It uses a real <see cref="ZoneSigningService" /> over a SQLite database.
/// </summary>
public sealed class ZoneSigningServiceCanonicalFormTests : IAsyncLifetime
{
    private const string ZoneName = "Mixed.Example.Com";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-canonical-form-tests-{Guid.NewGuid()}.sqlite3"
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
                DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec,
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

        foreach (string name in new[] { $"WWW.{ZoneName}", $"*.{ZoneName}" })
        {
            await recordRepository.AddAsync(
                new RecordEntity
                {
                    Id = Guid.NewGuid(),
                    ZoneId = _zoneId,
                    Name = name,
                    Type = DnsResourceRecordTypeEnum.A,
                    Class = DnsClassEnum.Internet,
                    TimeToLiveSeconds = 300,
                    DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                        new ARecordData(System.Net.IPAddress.Parse("192.0.2.20"))
                    ),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        }
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
    public async Task ASignedZoneWithMixedCaseNamesHasEveryNsecAndAddressRrsetVerifying()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneSigningService signingService =
            scope.ServiceProvider.GetRequiredService<IZoneSigningService>();

        SignedZoneData signed = await signingService.SignZoneAsync(_zoneId);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<DnsResourceRecord> nsecRecords =
        [
            .. signed.DenialOfExistenceRecords.Where(record =>
                record.Type == DnsResourceRecordTypeEnum.Nsec
            ),
        ];
        Assert.True(nsecRecords.Count >= 3);

        // Each NSEC is its own RRset (one record per owner name).
        foreach (DnsResourceRecord nsec in nsecRecords)
            Assert.True(
                RrsetSignatureVerifier.TryVerify(
                    [nsec],
                    signed.GetSignatures(nsec.Name, DnsResourceRecordTypeEnum.Nsec),
                    signed.DnsKeyRecords,
                    now
                ),
                $"NSEC at {nsec.Name} does not verify."
            );

        // The A RRset at the mixed-case owner name.
        DnsName www = DnsName.Parse($"www.{ZoneName}");
        List<DnsResourceRecord> wwwRrset = [AddressRecord(www)];
        Assert.True(
            RrsetSignatureVerifier.TryVerify(
                wwwRrset,
                signed.GetSignatures(www, DnsResourceRecordTypeEnum.A),
                signed.DnsKeyRecords,
                now
            )
        );
    }

    [Fact]
    public async Task AWildcardRecordOfASignedZoneVerifiesWhenPresentedAsAnExpansion()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneSigningService signingService =
            scope.ServiceProvider.GetRequiredService<IZoneSigningService>();

        SignedZoneData signed = await signingService.SignZoneAsync(_zoneId);

        // The RRSIG is stored at the wildcard (Labels counts the zone's three labels) and an
        // authoritative answer carries it unchanged next to the expanded owner name.
        DnsName wildcard = DnsName.Parse($"*.{ZoneName}");
        DnsName expanded = DnsName.Parse($"host.sub.{ZoneName}");
        IReadOnlyList<DnsResourceRecord> stored = signed.GetSignatures(
            wildcard,
            DnsResourceRecordTypeEnum.A
        );
        Assert.NotEmpty(stored);

        List<DnsResourceRecord> presentedRrset = [AddressRecord(expanded)];
        List<DnsResourceRecord> presentedRrsigs =
        [
            .. stored.Select(record => new DnsResourceRecord(
                expanded,
                record.Type,
                record.Class,
                record.TimeToLive,
                record.Data
            )),
        ];

        Assert.True(
            RrsetSignatureVerifier.TryVerify(
                presentedRrset,
                presentedRrsigs,
                signed.DnsKeyRecords,
                DateTimeOffset.UtcNow
            )
        );
    }

    /// <summary>
    ///     This method builds the A record the zone is seeded with, at the given owner name.
    /// </summary>
    private static DnsResourceRecord AddressRecord(DnsName owner) =>
        new(
            owner,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(System.Net.IPAddress.Parse("192.0.2.20"))
            )
        );
}
