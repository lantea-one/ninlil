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
///     This class runs a real, live, wall-clock RFC 6781 §4.1.1.1 Pre-Publish Zone-Signing-Key
///     rollover end to end -- <see cref="DnssecKeyRolloverBackgroundService" />'s own real
///     scheduled loop, ticking against a real host and a real EF Core SQLite database, driven by
///     genuinely compressed timers (a couple of real seconds, not the days/weeks a real-world
///     rollover would actually take) rather than a mocked clock. At every single point across the
///     whole rollover, the zone's own currently-live signed answer for an ordinary A record is
///     re-verified, cryptographically, against whatever DNSKEY RRset is live at that exact moment
///     -- via <see cref="RrsetSignatureVerifier.TryVerify" />, the same primitive
///     <see cref="Ninlil.Dns.Server.Recursive.Validation.DnssecValidator" /> itself calls -- proving
///     this server never actually serves a signature a real validator would reject, at any instant
///     during the transition. The specific pre-publish overlap this scheme exists to guarantee is
///     also proven directly: a signature captured from *before* the new key was even generated
///     keeps verifying against every later DNSKEY RRset snapshot for as long as the old key
///     remains published, and only stops once it is genuinely, finally withdrawn -- the exact
///     boundary RFC 6781 itself draws.
/// </summary>
public sealed class DnssecKeyRolloverZskPrePublishLiveTests : IAsyncLifetime
{
    private const string ZoneName = "zsk-prepublish.example.com";
    private const string RecordName = $"www.{ZoneName}";

    /// <summary>
    ///     Every timed wait this test's own zone drives (the DNSKEY RRset TTL/max zone TTL via the
    ///     SOA/A record TTLs below, and <see cref="DnssecKeyRolloverOptions.PropagationDelay" />)
    ///     is measured in real seconds, deliberately short enough for a real rollover's own two
    ///     wait windows to complete inside an ordinary test timeout, while still being long enough
    ///     to genuinely separate "before," "during," and "after" in wall-clock time rather than
    ///     collapsing into a single tick.
    /// </summary>
    private static readonly TimeSpan RecordTtl = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan PropagationDelay = TimeSpan.FromMilliseconds(400);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-zsk-prepublish-live-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private Guid _zoneId;
    private Guid _originalZskId;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Configuration["DnsServer:Authoritative:DnssecKeyRollover:PropagationDelay"] =
            PropagationDelay.ToString();
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
                Name = RecordName,
                Type = DnsResourceRecordTypeEnum.A,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = (uint)RecordTtl.TotalSeconds,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new ARecordData(IPAddress.Parse("192.0.2.40"))
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        DnssecKeyEntity originalZsk = await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.Ed25519
        );
        _originalZskId = originalZsk.Id;

        await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );

        // Runs SignedZoneCache's and DnssecKeyRolloverBackgroundService's own initial startup --
        // the rollover background service starts polling immediately, on its own now-compressed
        // schedule, exactly as it would in a real, running instance of this project.
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    /// <summary>
    ///     This method builds the exact wire-format A rrset <see cref="ZoneSigningService" /> itself
    ///     signs, so this test can verify a captured RRSIG against it the same way a real validator
    ///     would.
    /// </summary>
    private static List<DnsResourceRecord> BuildARrset() =>
        [
            new DnsResourceRecord(
                DnsName.Parse(RecordName),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                (uint)RecordTtl.TotalSeconds,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(IPAddress.Parse("192.0.2.40"))
                )
            ),
        ];

    [Fact]
    public async Task ARealZskRolloverPromotesTheNewKeyAndRemovesTheOldOneWithoutEverServingABogusSignature()
    {
        List<DnsResourceRecord> aRrset = BuildARrset();

        SignedZoneCache signedZoneCache = _host.Services.GetRequiredService<SignedZoneCache>();

        SignedZoneData initial = signedZoneCache.GetSignedData(_zoneId);
        Assert.NotNull(initial);
        Assert.Equal(2, initial.DnsKeyRecords.Count); // original ZSK + KSK
        Assert.True(
            RrsetSignatureVerifier.TryVerify(
                aRrset,
                initial.GetSignatures(DnsName.Parse(RecordName), DnsResourceRecordTypeEnum.A),
                initial.DnsKeyRecords,
                DateTimeOffset.UtcNow
            )
        );

        // A resolver's own cached copy of the pre-rollover answer -- captured once, right before
        // the rollover starts, and never refreshed for the rest of this test. Pre-Publish's whole
        // purpose is to keep this exact signature verifiable against every later DNSKEY RRset,
        // right up until this specific key is finally, genuinely withdrawn.
        IReadOnlyList<DnsResourceRecord> signatureCapturedBeforeRollover = initial.GetSignatures(
            DnsName.Parse(RecordName),
            DnsResourceRecordTypeEnum.A
        );

        await using (AsyncServiceScope startScope = _host.Services.CreateAsyncScope())
        {
            IDnssecKeyManagementService keyManagement =
                startScope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();

            // The one manual step a real operator (or DnssecKeysController.Roll) performs --
            // everything after this is DnssecKeyRolloverBackgroundService's own automated walk.
            await keyManagement.StartRolloverAsync(_zoneId, _originalZskId);
        }

        await signedZoneCache.ReloadZoneAsync(_zoneId);

        SignedZoneData afterPublish = signedZoneCache.GetSignedData(_zoneId);
        Assert.Equal(3, afterPublish.DnsKeyRecords.Count); // old ZSK (still signing) + new ZSK (published only) + KSK

        // The newly published key does not sign yet -- the A rrset's own RRSIG set must be
        // unchanged from before the rollover even started.
        Assert.Equal(
            signatureCapturedBeforeRollover.Count,
            afterPublish.GetSignatures(DnsName.Parse(RecordName), DnsResourceRecordTypeEnum.A).Count
        );

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        bool oldKeyRemoved = false;
        bool newKeyPromoted = false;
        bool cacheWithdrewOldKey = false;

        // The real, live, continuously-polled proof: for as long as this rollover is in flight,
        // the zone's own currently-live answer must verify against whatever DNSKEY RRset is
        // currently live, at every single point this test happens to look -- and the pre-rollover
        // signature captured above must keep verifying too, for as long as its own key remains
        // published (RFC 6781's own "old RRSIGs are still verifiable" guarantee, checked live
        // rather than assumed). The background service saves the new lifecycle states before it
        // reloads the signed zone, so the database can report the rollover finished a moment
        // before the cache serves it; the loop waits for both.
        while (
            DateTimeOffset.UtcNow < deadline
            && !(oldKeyRemoved && newKeyPromoted && cacheWithdrewOldKey)
        )
        {
            await Task.Delay(150);

            SignedZoneData current = signedZoneCache.GetSignedData(_zoneId);
            Assert.NotNull(current);

            IReadOnlyList<DnsResourceRecord> currentSignatures = current.GetSignatures(
                DnsName.Parse(RecordName),
                DnsResourceRecordTypeEnum.A
            );

            Assert.True(
                RrsetSignatureVerifier.TryVerify(
                    aRrset,
                    currentSignatures,
                    current.DnsKeyRecords,
                    DateTimeOffset.UtcNow
                ),
                "The zone's own currently-live signed answer failed to verify against its own currently-live DNSKEY RRset -- a real bogus signature would have been served at this exact instant."
            );

            await using AsyncServiceScope keyScope = _host.Services.CreateAsyncScope();
            IDnssecKeyRepository liveKeyRepository =
                keyScope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
            IReadOnlyList<DnssecKeyEntity> keys = await liveKeyRepository.ListByZoneIdAsync(
                _zoneId
            );
            DnssecKeyEntity originalZsk = keys.Single(key => key.Id == _originalZskId);

            oldKeyRemoved = originalZsk.LifecycleState == DnssecKeyLifecycleStateEnum.Removed;
            newKeyPromoted = keys.Any(key =>
                key.Id != _originalZskId
                && key.Role == DnssecKeyRoleEnum.Zsk
                && key.LifecycleState == DnssecKeyLifecycleStateEnum.Active
            );
            cacheWithdrewOldKey = current.DnsKeyRecords.Count == 2;

            if (!oldKeyRemoved)
                Assert.True(
                    RrsetSignatureVerifier.TryVerify(
                        aRrset,
                        signatureCapturedBeforeRollover,
                        current.DnsKeyRecords,
                        DateTimeOffset.UtcNow
                    ),
                    "The signature a resolver would have cached before the rollover started stopped verifying while its own key was still published -- Pre-Publish's own continuity guarantee was violated."
                );
        }

        Assert.True(
            oldKeyRemoved,
            "The original ZSK was never withdrawn within the test's own deadline."
        );
        Assert.True(
            newKeyPromoted,
            "The incoming ZSK never became Active within the test's own deadline."
        );
        Assert.True(
            cacheWithdrewOldKey,
            "The signed zone never stopped serving the original ZSK within the test's own deadline."
        );

        SignedZoneData final = signedZoneCache.GetSignedData(_zoneId);
        Assert.Equal(2, final.DnsKeyRecords.Count); // new ZSK + KSK only, old ZSK fully withdrawn

        // The pre-rollover signature's own key is genuinely gone now -- it correctly stops
        // verifying, the real, expected end state a resolver's own cache TTL would have already
        // caught up to in the wild by this point.
        Assert.False(
            RrsetSignatureVerifier.TryVerify(
                aRrset,
                signatureCapturedBeforeRollover,
                final.DnsKeyRecords,
                DateTimeOffset.UtcNow
            )
        );

        Assert.True(
            RrsetSignatureVerifier.TryVerify(
                aRrset,
                final.GetSignatures(DnsName.Parse(RecordName), DnsResourceRecordTypeEnum.A),
                final.DnsKeyRecords,
                DateTimeOffset.UtcNow
            )
        );
    }
}
