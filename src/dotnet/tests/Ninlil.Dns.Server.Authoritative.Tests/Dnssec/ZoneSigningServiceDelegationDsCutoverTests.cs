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
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class covers <see cref="ZoneSigningService" />'s own private
///     <c>ComputeDelegationDsRecordsAsync</c> method and its
///     real-time dependency on a delegation's own currently-canonical Key-Signing Key -- the
///     "CAA/DS-adjacent automation" a locally hosted KSK Double-Signature rollover builds directly
///     on: whichever KSK currently holds <see cref="DnssecKeyLifecycleStateEnum.Active" /> is
///     exactly the one KSK the parent zone's own next sign publishes a DS record for, with no
///     operator action needed at all, as long as both zones are hosted on this same server. Proven
///     here fast and deterministically, by manipulating a child zone's own key states directly and
///     re-signing the parent zone on demand, rather than waiting out
///     <see cref="DnssecKeyRolloverBackgroundService" />'s own real timers -- those are already
///     proven live, end to end, by <see cref="DnssecKeyRolloverKskDoubleSignatureLiveTests" />
///     for the genuinely external-parent case that same automation cannot reach.
/// </summary>
public sealed class ZoneSigningServiceDelegationDsCutoverTests : IAsyncLifetime
{
    private const string ParentZoneName = "delegation-ds-cutover.example.com";
    private const string ChildZoneName = "child.delegation-ds-cutover.example.com";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-delegation-ds-cutover-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private Guid _parentZoneId;
    private Guid _childZoneId;
    private DnssecKeyEntity _firstChildKsk;
    private DnssecKeyEntity _secondChildKsk;

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

        _parentZoneId = Guid.NewGuid();
        _childZoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _parentZoneId,
                Name = ParentZoneName,
                Type = ZoneTypeEnum.Primary,
                IsDnssecSigned = true,
                DnssecAlgorithm = DnssecAlgorithmEnum.Ed25519,
                DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _childZoneId,
                Name = ChildZoneName,
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
                ZoneId = _parentZoneId,
                Name = ParentZoneName,
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{ParentZoneName}"),
                        DnsName.Parse($"admin.{ParentZoneName}"),
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

        // The delegation itself -- the real trigger ComputeDelegationDsRecordsAsync's own
        // delegation-point detection looks for.
        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _parentZoneId,
                Name = ChildZoneName,
                Type = DnsResourceRecordTypeEnum.NS,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new NsRecordData(DnsName.Parse($"ns1.{ChildZoneName}"))
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _childZoneId,
                Name = ChildZoneName,
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{ChildZoneName}"),
                        DnsName.Parse($"admin.{ChildZoneName}"),
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
        await unitOfWork.SaveChangesAsync();

        // The parent needs a real key too, or it is not itself signable at all.
        await keyManagement.GenerateKeyAsync(
            _parentZoneId,
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.Ed25519
        );
        await keyManagement.GenerateKeyAsync(
            _parentZoneId,
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );

        await keyManagement.GenerateKeyAsync(
            _childZoneId,
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.Ed25519
        );

        // Two real KSKs for the child -- as if a Double-Signature rollover were already mid-flight,
        // constructed directly rather than waiting on DnssecKeyRolloverBackgroundService's own real
        // timers (proven separately, live, by DnssecKeyRolloverKskDoubleSignatureLiveTests).
        _firstChildKsk = await keyManagement.GenerateKeyAsync(
            _childZoneId,
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );
        _secondChildKsk = await keyManagement.GenerateKeyAsync(
            _childZoneId,
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );

        IDnssecKeyRepository childKeyRepository =
            seedScope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();

        // _firstChildKsk stays Active (the ordinary, already-generated state); _secondChildKsk
        // starts the rollover's own DoubleSignature state, exactly what StartRolloverAsync itself
        // would have produced.
        _secondChildKsk.LifecycleState = DnssecKeyLifecycleStateEnum.DoubleSignature;
        childKeyRepository.Update(_secondChildKsk);
        await unitOfWork.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task TheParentsGeneratedDsFollowsWhicheverChildKskIsCurrentlyActive()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneSigningService signingService =
            scope.ServiceProvider.GetRequiredService<IZoneSigningService>();
        IDnssecKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        SignedZoneData beforeCutover = await signingService.SignZoneAsync(_parentZoneId);
        DnsName delegationName = DnsName.Parse(ChildZoneName);

        Assert.True(
            beforeCutover.GeneratedDsRecordsByDelegationName.TryGetValue(
                delegationName,
                out IReadOnlyList<DnsResourceRecord> dsBeforeCutover
            )
        );

        // Exactly one DS, naming the currently-Active KSK -- not both, even though both are
        // genuinely co-signing the child's own DNSKEY RRset right now -- matching RFC 6781 §4.1.2
        // Figure 4's own single-DS-at-a-time Double-Signature shape.
        DsRecordData dsDataBeforeCutover = Assert.Single(
            dsBeforeCutover.Select(record =>
                (DsRecordData)
                    DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DS, record.Data)
            )
        );
        Assert.Equal(_firstChildKsk.KeyTag, dsDataBeforeCutover.KeyTag);

        // Simulate the real cutover DnssecKeyRolloverBackgroundService itself performs once the
        // incoming key's own pre-roll wait (and, for a genuinely external parent, an operator's
        // own confirmation) is satisfied. Re-fetched fresh within this scope's own DbContext first
        // -- SignZoneAsync above already loaded and tracked these same rows in this very scope, so
        // reusing the InitializeAsync-scope instances directly would collide with EF Core's own
        // identity map (the same cross-scope staleness trap this project's own bug catalog already
        // documents, encountered here in the opposite direction: stale detached instances, not a
        // stale tracked one).
        DnssecKeyEntity firstChildKskInThisScope = await keyRepository.GetByIdAsync(
            _firstChildKsk.Id
        );
        DnssecKeyEntity secondChildKskInThisScope = await keyRepository.GetByIdAsync(
            _secondChildKsk.Id
        );
        firstChildKskInThisScope.LifecycleState = DnssecKeyLifecycleStateEnum.DoubleSignature;
        firstChildKskInThisScope.RetiredAtUtc = DateTimeOffset.UtcNow;
        secondChildKskInThisScope.LifecycleState = DnssecKeyLifecycleStateEnum.Active;
        keyRepository.Update(firstChildKskInThisScope);
        keyRepository.Update(secondChildKskInThisScope);
        await unitOfWork.SaveChangesAsync();

        SignedZoneData afterCutover = await signingService.SignZoneAsync(_parentZoneId);

        Assert.True(
            afterCutover.GeneratedDsRecordsByDelegationName.TryGetValue(
                delegationName,
                out IReadOnlyList<DnsResourceRecord> dsAfterCutover
            )
        );

        DsRecordData dsDataAfterCutover = Assert.Single(
            dsAfterCutover.Select(record =>
                (DsRecordData)
                    DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DS, record.Data)
            )
        );

        // The parent's own next sign follows the cutover automatically -- the real DS now names
        // the second key, with no operator action of any kind, since both zones are hosted here.
        Assert.Equal(_secondChildKsk.KeyTag, dsDataAfterCutover.KeyTag);
        Assert.NotEqual(dsDataBeforeCutover.KeyTag, dsDataAfterCutover.KeyTag);
    }
}
