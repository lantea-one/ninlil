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
using Ninlil.Dns.Server.Recursive.Validation;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class runs an RFC 6781 §4.1.2 Double-Signature KSK rollover end to end with the real
///     <see cref="DnssecKeyRolloverBackgroundService" /> loop on a host with a temporary SQLite
///     database and short timers, for a zone whose parent is not hosted here. It checks that the
///     incoming KSK is never promoted, however long it waits, until an operator confirms the
///     parent's DS, and that after confirmation both KSKs sign the DNSKEY RRset throughout,
///     verifying signatures against the DNSKEY RRset live at each step.
/// </summary>
public sealed class DnssecKeyRolloverKskDoubleSignatureLiveTests : IAsyncLifetime
{
    private const string ZoneName = "ksk-doublesig.example.com";

    private static readonly TimeSpan RecordTtl = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PropagationDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ExternalParentDsTtl = TimeSpan.FromMilliseconds(500);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-ksk-doublesig-live-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private Guid _zoneId;
    private Guid _originalKskId;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Configuration["DnsServer:Authoritative:DnssecKeyRollover:PropagationDelay"] =
            PropagationDelay.ToString();
        builder.Configuration["DnsServer:Authoritative:DnssecKeyRollover:ExternalParentDsTtl"] =
            ExternalParentDsTtl.ToString();
        builder.Configuration["DnsServer:Authoritative:DnssecKeyRollover:PollInterval"] =
            "00:00:00.250";
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

        // No parent zone is created, so the delegation is external and the rollover needs operator
        // confirmation.
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
                TimeToLiveSeconds = (uint)RecordTtl.TotalSeconds,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{ZoneName}"),
                        DnsName.Parse($"admin.{ZoneName}"),
                        1,
                        3600,
                        900,
                        604800,
                        (uint)RecordTtl.TotalSeconds
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
                TimeToLiveSeconds = (uint)RecordTtl.TotalSeconds,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new ARecordData(IPAddress.Parse("192.0.2.50"))
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

        DnssecKeyEntity originalKsk = await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );
        _originalKskId = originalKsk.Id;

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task AnExternalKskRolloverWaitsForOperatorConfirmationBeforePromotingAndThenCoSignsThroughout()
    {
        DnsName apexName = DnsName.Parse(ZoneName);
        SignedZoneCache signedZoneCache = _host.Services.GetRequiredService<SignedZoneCache>();

        SignedZoneData initial = signedZoneCache.GetSignedData(_zoneId);
        Assert.NotNull(initial);
        Assert.Equal(2, initial.DnsKeyRecords.Count); // ZSK + original KSK

        Guid newKskId;
        await using (AsyncServiceScope startScope = _host.Services.CreateAsyncScope())
        {
            IDnssecKeyManagementService keyManagement =
                startScope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();
            DnssecKeyEntity newKsk = await keyManagement.StartRolloverAsync(
                _zoneId,
                _originalKskId
            );
            newKskId = newKsk.Id;
        }

        await signedZoneCache.ReloadZoneAsync(_zoneId);

        SignedZoneData afterPublish = signedZoneCache.GetSignedData(_zoneId);
        Assert.Equal(3, afterPublish.DnsKeyRecords.Count); // ZSK + old KSK + new KSK

        // Both KSKs already co-sign the DNSKEY RRset from the very first moment the new one
        // exists -- RFC 6781 §4.1.2's Figure 4 shows both RRSIGs present starting at its own
        // "new DNSKEY" stage, unlike a ZSK's Pre-Publish incoming key, which signs nothing yet.
        Assert.Equal(
            2,
            afterPublish.GetSignatures(apexName, DnsResourceRecordTypeEnum.DnsKey).Count
        );
        Assert.True(
            RrsetSignatureVerifier.TryVerify(
                afterPublish.DnsKeyRecords,
                afterPublish.GetSignatures(apexName, DnsResourceRecordTypeEnum.DnsKey),
                afterPublish.DnsKeyRecords,
                DateTimeOffset.UtcNow
            )
        );

        // Wait well past the incoming key's pre-roll wait (DNSKEY RRset TTL + propagation
        // delay) with no operator confirmation given at all -- the real safety gate this scheme
        // needs for a external parent: DnssecKeyRolloverBackgroundService must never
        // promote it regardless of how long that wait has elapsed on its own.
        await Task.Delay(RecordTtl + PropagationDelay + TimeSpan.FromSeconds(1));

        await using (AsyncServiceScope guardScope = _host.Services.CreateAsyncScope())
        {
            IDnssecKeyRepository guardKeyRepository =
                guardScope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
            DnssecKeyEntity newKskStillWaiting = (
                await guardKeyRepository.ListByZoneIdAsync(_zoneId)
            ).Single(key => key.Id == newKskId);

            Assert.Equal(
                DnssecKeyLifecycleStateEnum.DoubleSignature,
                newKskStillWaiting.LifecycleState
            );
        }

        // The one manual step a real operator (or DnssecKeysController.ConfirmParentDsUpdated)
        // performs once they have updated the DS record at their real registrar.
        await using (AsyncServiceScope confirmScope = _host.Services.CreateAsyncScope())
        {
            IDnssecKeyManagementService keyManagement =
                confirmScope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();
            await keyManagement.ConfirmParentDsUpdatedAsync(newKskId);
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        bool oldKeyRemoved = false;
        bool newKeyPromoted = false;
        bool cacheWithdrewOldKey = false;

        // The background service saves the new lifecycle states before it reloads the signed
        // zone, so the database can report the rollover finished a moment before the cache
        // serves it; the loop waits for both.
        while (
            DateTimeOffset.UtcNow < deadline
            && !(oldKeyRemoved && newKeyPromoted && cacheWithdrewOldKey)
        )
        {
            await Task.Delay(150);

            SignedZoneData current = signedZoneCache.GetSignedData(_zoneId);
            Assert.NotNull(current);

            IReadOnlyList<DnsResourceRecord> dnsKeySignatures = current.GetSignatures(
                apexName,
                DnsResourceRecordTypeEnum.DnsKey
            );

            // The real, live, continuously polled proof: the DNSKEY RRset itself must always
            // verify against its currently-live key set, at every single point this test
            // happens to look, throughout the whole transition.
            Assert.True(
                RrsetSignatureVerifier.TryVerify(
                    current.DnsKeyRecords,
                    dnsKeySignatures,
                    current.DnsKeyRecords,
                    DateTimeOffset.UtcNow
                ),
                "The zone's own currently-live DNSKEY RRset failed to verify against itself -- a real bogus signature would have been served at this exact instant."
            );

            await using AsyncServiceScope keyScope = _host.Services.CreateAsyncScope();
            IDnssecKeyRepository liveKeyRepository =
                keyScope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
            IReadOnlyList<DnssecKeyEntity> keys = await liveKeyRepository.ListByZoneIdAsync(
                _zoneId
            );
            DnssecKeyEntity originalKsk = keys.Single(key => key.Id == _originalKskId);
            DnssecKeyEntity newKsk = keys.Single(key => key.Id == newKskId);

            oldKeyRemoved = originalKsk.LifecycleState == DnssecKeyLifecycleStateEnum.Removed;
            newKeyPromoted = newKsk.LifecycleState == DnssecKeyLifecycleStateEnum.Active;
            cacheWithdrewOldKey = current.DnsKeyRecords.Count == 2;

            // Until the old key is removed, both KSKs must still be co-signing the
            // DNSKEY RRset -- RFC 6781 §4.1.2's guarantee that a resolver holding
            // either key as its trust anchor stays validated throughout.
            if (!oldKeyRemoved)
                Assert.Equal(2, dnsKeySignatures.Count);
        }

        Assert.True(
            oldKeyRemoved,
            "The original KSK was never withdrawn within the test's own deadline."
        );
        Assert.True(
            newKeyPromoted,
            "The incoming KSK never became Active within the test's own deadline."
        );
        Assert.True(
            cacheWithdrewOldKey,
            "The signed zone never stopped serving the original KSK within the test's own deadline."
        );

        SignedZoneData final = signedZoneCache.GetSignedData(_zoneId);
        Assert.Equal(2, final.DnsKeyRecords.Count); // ZSK + new KSK only, old KSK fully withdrawn
        Assert.Single(final.GetSignatures(apexName, DnsResourceRecordTypeEnum.DnsKey));
        Assert.True(
            RrsetSignatureVerifier.TryVerify(
                final.DnsKeyRecords,
                final.GetSignatures(apexName, DnsResourceRecordTypeEnum.DnsKey),
                final.DnsKeyRecords,
                DateTimeOffset.UtcNow
            )
        );
    }
}
