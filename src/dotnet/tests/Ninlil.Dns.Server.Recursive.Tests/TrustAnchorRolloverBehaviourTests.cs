using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Support;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests which keys <see cref="TrustAnchorRolloverService" /> accepts an update from
///     (RFC 5011 §2.1, §4: only Valid or Missing anchors, and a revocation only when the revoked
///     key signs the RRset itself) and when it checks (RFC 5011 §2.3). It runs the service on
///     <see cref="TrustAnchorRolloverHarness" />, a scripted engine and a manual clock, with
///     signed DNSKEY RRsets from <see cref="DnssecTestFixture" />.
/// </summary>
public sealed class TrustAnchorRolloverBehaviourTests
{
    private static readonly RecursiveServerOptions DefaultOptions = new()
    {
        EnableAutomaticTrustAnchorUpdate = true,
    };

    private static PersistedTrustAnchor Anchor(
        DnssecTestFixture fixture,
        DnssecTestFixture.ZoneKey key,
        TrustAnchorStateEnum state,
        DateTimeOffset? missingSince = null
    )
    {
        TrustAnchor shape = fixture.BuildTrustAnchor(DnsName.Root, key);

        return new PersistedTrustAnchor(
            shape.KeyTag,
            shape.Algorithm,
            shape.DigestType,
            shape.Digest,
            state,
            DateTimeOffset.UtcNow.AddDays(-400),
            state == TrustAnchorStateEnum.AddPend ? null : DateTimeOffset.UtcNow.AddDays(-370),
            missingSince
        );
    }

    /// <summary>
    ///     This method builds the form of a key with the REVOKE bit set. Setting the bit changes the
    ///     key tag, so the returned key signs with the new tag, as a revoking zone owner does.
    /// </summary>
    private static DnssecTestFixture.ZoneKey Revoked(DnssecTestFixture.ZoneKey key)
    {
        DnsKeyRecordData data = new(
            true,
            false,
            DnssecAlgorithmEnum.Ed25519,
            key.KeyPair.PublicKeyWireFormat,
            isRevoked: true
        );
        byte[] rdata = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.DnsKey, data);
        DnsResourceRecord record = new(
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            key.DnsKeyRecord.TimeToLive,
            rdata
        );

        return key with
        {
            DnsKeyRecord = record,
            KeyTag = DnssecKeyTag.Compute(DnssecAlgorithmEnum.Ed25519, rdata),
        };
    }

    private static IterativeResolutionResult Success(
        IReadOnlyList<DnsResourceRecord> keys,
        params DnsResourceRecord[] rrsigs
    ) => new(RecursiveResolutionStatusEnum.Success, keys, rrsigs, null, []);

    private static IterativeResolutionResult Failure() =>
        new(RecursiveResolutionStatusEnum.ServFail, [], [], null, []);

    private static PersistedTrustAnchor Find(
        TrustAnchorRolloverHarness harness,
        TrustAnchor shape
    ) => harness.Store.Current.Single(anchor => anchor.KeyTag == shape.KeyTag);

    [Fact]
    public async Task FirstCheck_RunsOneMinuteAfterStart()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        await using TrustAnchorRolloverHarness harness = new(
            [Anchor(fixture, keyA, TrustAnchorStateEnum.Valid)],
            DefaultOptions
        );
        harness.Result = Success(
            [keyA.DnsKeyRecord],
            fixture.Sign(keyA, DnsName.Root, [keyA.DnsKeyRecord])
        );

        await harness.AdvanceAsync(TimeSpan.FromSeconds(59));
        Assert.Equal(0, harness.Checks);

        await harness.AdvanceAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, harness.Checks);
    }

    [Fact]
    public async Task NextCheck_AfterASuccess_IsHalfTheOriginalTtl()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root, ttl: 172_800);
        await using TrustAnchorRolloverHarness harness = new(
            [Anchor(fixture, keyA, TrustAnchorStateEnum.Valid)],
            DefaultOptions
        );
        harness.Result = Success(
            [keyA.DnsKeyRecord],
            fixture.Sign(
                keyA,
                DnsName.Root,
                [keyA.DnsKeyRecord],
                expiration: DateTimeOffset.UtcNow.AddDays(10)
            )
        );

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(1, harness.Checks);

        // Half of 172 800 s is one day; half the 10 days to expiry is five.
        await harness.AdvanceAsync(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1));
        Assert.Equal(1, harness.Checks);

        await harness.AdvanceAsync(TimeSpan.FromMinutes(2));
        Assert.Equal(2, harness.Checks);
    }

    [Fact]
    public async Task NextCheck_AfterASuccess_NeverExceedsTheConfiguredInterval()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root, ttl: 172_800);
        RecursiveServerOptions options = new()
        {
            EnableAutomaticTrustAnchorUpdate = true,
            TrustAnchorRefreshInterval = TimeSpan.FromHours(12),
        };
        await using TrustAnchorRolloverHarness harness = new(
            [Anchor(fixture, keyA, TrustAnchorStateEnum.Valid)],
            options
        );
        harness.Result = Success(
            [keyA.DnsKeyRecord],
            fixture.Sign(
                keyA,
                DnsName.Root,
                [keyA.DnsKeyRecord],
                expiration: DateTimeOffset.UtcNow.AddDays(10)
            )
        );

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));
        await harness.AdvanceAsync(TimeSpan.FromHours(12) - TimeSpan.FromMinutes(1));
        Assert.Equal(1, harness.Checks);

        await harness.AdvanceAsync(TimeSpan.FromMinutes(2));
        Assert.Equal(2, harness.Checks);
    }

    [Fact]
    public async Task NextCheck_AfterAFailure_IsOneHourUntilATtlIsKnownThenTheRetryFormula()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root, ttl: 172_800);
        await using TrustAnchorRolloverHarness harness = new(
            [Anchor(fixture, keyA, TrustAnchorStateEnum.Valid)],
            DefaultOptions
        );
        harness.Result = Failure();

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(1, harness.Checks);

        // No successful check yet, so the retry waits one hour.
        await harness.AdvanceAsync(TimeSpan.FromMinutes(59));
        Assert.Equal(1, harness.Checks);
        harness.Result = Success(
            [keyA.DnsKeyRecord],
            fixture.Sign(
                keyA,
                DnsName.Root,
                [keyA.DnsKeyRecord],
                expiration: DateTimeOffset.UtcNow.AddDays(10)
            )
        );
        await harness.AdvanceAsync(TimeSpan.FromMinutes(2));
        Assert.Equal(2, harness.Checks);

        // The success schedules the next check a day on; that check fails.
        harness.Result = Failure();
        await harness.AdvanceAsync(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        Assert.Equal(3, harness.Checks);

        // retryTime = MAX(1 h, MIN(1 day, OrigTTL / 10 = 4.8 h, expiry / 10 = 21.6 h)) = 4.8 h.
        await harness.AdvanceAsync(TimeSpan.FromHours(4));
        Assert.Equal(3, harness.Checks);
        await harness.AdvanceAsync(TimeSpan.FromMinutes(49));
        Assert.Equal(4, harness.Checks);
    }

    [Fact]
    public async Task AnRrsetSignedOnlyByAnAddPendKey_ChangesNothingAndWarns()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey valid = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey pending = fixture.GenerateZoneKey(DnsName.Root);
        await using TrustAnchorRolloverHarness harness = new(
            [
                Anchor(fixture, valid, TrustAnchorStateEnum.Valid),
                Anchor(fixture, pending, TrustAnchorStateEnum.AddPend),
            ],
            DefaultOptions
        );
        harness.Result = Success(
            [pending.DnsKeyRecord],
            fixture.Sign(pending, DnsName.Root, [pending.DnsKeyRecord])
        );

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(1, harness.Checks);
        Assert.Equal(0, harness.Store.SaveCount);
        Assert.Contains(LogLevel.Warning, harness.Logger.Levels);
    }

    [Theory]
    [InlineData(TrustAnchorStateEnum.Removed)]
    [InlineData(TrustAnchorStateEnum.Revoked)]
    public async Task AnRrsetSignedOnlyByARemovedOrRevokedAnchor_ChangesNothing(
        TrustAnchorStateEnum state
    )
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey valid = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey retired = fixture.GenerateZoneKey(DnsName.Root);
        await using TrustAnchorRolloverHarness harness = new(
            [Anchor(fixture, valid, TrustAnchorStateEnum.Valid), Anchor(fixture, retired, state)],
            DefaultOptions
        );
        harness.Result = Success(
            [retired.DnsKeyRecord],
            fixture.Sign(retired, DnsName.Root, [retired.DnsKeyRecord])
        );

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(1, harness.Checks);
        Assert.Equal(0, harness.Store.SaveCount);
        Assert.Contains(LogLevel.Warning, harness.Logger.Levels);
    }

    [Fact]
    public async Task AMissingAnchorThatReappearsAndSignsTheRrset_ReturnsToValid()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey missing = fixture.GenerateZoneKey(DnsName.Root);
        await using TrustAnchorRolloverHarness harness = new(
            [
                Anchor(
                    fixture,
                    missing,
                    TrustAnchorStateEnum.Missing,
                    DateTimeOffset.UtcNow.AddDays(-1)
                ),
            ],
            DefaultOptions
        );
        harness.Result = Success(
            [missing.DnsKeyRecord],
            fixture.Sign(missing, DnsName.Root, [missing.DnsKeyRecord])
        );

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));

        PersistedTrustAnchor saved = Find(harness, fixture.BuildTrustAnchor(DnsName.Root, missing));
        Assert.Equal(TrustAnchorStateEnum.Valid, saved.State);
        Assert.Null(saved.MissingSinceUtc);
    }

    [Fact]
    public async Task ARevokedKeyThatDidNotSignItsOwnRevocation_StaysValid()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey keyB = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey keyBRevoked = Revoked(keyB);
        await using TrustAnchorRolloverHarness harness = new(
            [
                Anchor(fixture, keyA, TrustAnchorStateEnum.Valid),
                Anchor(fixture, keyB, TrustAnchorStateEnum.Valid),
            ],
            DefaultOptions
        );

        // An attacker who replays the RRset with B's REVOKE bit set cannot produce B's signature.
        List<DnsResourceRecord> rrset = [keyA.DnsKeyRecord, keyBRevoked.DnsKeyRecord];
        harness.Result = Success(rrset, fixture.Sign(keyA, DnsName.Root, rrset));

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(
            TrustAnchorStateEnum.Valid,
            Find(harness, fixture.BuildTrustAnchor(DnsName.Root, keyB)).State
        );
        Assert.Equal(
            TrustAnchorStateEnum.Valid,
            Find(harness, fixture.BuildTrustAnchor(DnsName.Root, keyA)).State
        );
    }

    [Fact]
    public async Task ASelfSignedRevocation_RevokesTheAnchorAndPublishesTheEvent()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey keyB = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey keyBRevoked = Revoked(keyB);
        await using TrustAnchorRolloverHarness harness = new(
            [
                Anchor(fixture, keyA, TrustAnchorStateEnum.Valid),
                Anchor(fixture, keyB, TrustAnchorStateEnum.Valid),
            ],
            DefaultOptions
        );
        List<DnsResourceRecord> rrset = [keyA.DnsKeyRecord, keyBRevoked.DnsKeyRecord];
        harness.Result = Success(
            rrset,
            fixture.Sign(keyA, DnsName.Root, rrset),
            fixture.Sign(keyBRevoked, DnsName.Root, rrset)
        );

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));

        TrustAnchor shapeB = fixture.BuildTrustAnchor(DnsName.Root, keyB);
        Assert.Equal(TrustAnchorStateEnum.Revoked, Find(harness, shapeB).State);
        Assert.Equal(
            nameof(TrustAnchorStateEnum.Revoked),
            Assert.Single(harness.Publisher.Published.OfType<TrustAnchorRolloverEvent>()).NewState
        );
    }

    [Fact]
    public async Task ARevocationSignedOnlyByTheRevokedKey_IsRecognisedAndNothingElseChanges()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey keyB = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey keyBRevoked = Revoked(keyB);
        DnssecTestFixture.ZoneKey newcomer = fixture.GenerateZoneKey(
            DnsName.Root,
            isSecureEntryPoint: true
        );
        await using TrustAnchorRolloverHarness harness = new(
            [
                Anchor(fixture, keyA, TrustAnchorStateEnum.Valid),
                Anchor(fixture, keyB, TrustAnchorStateEnum.Valid),
            ],
            DefaultOptions
        );

        // RFC 5011 §2.1: the revoked key self-signs. No trusted, unrevoked key signed this RRset,
        // so only the revocation counts; the newcomer must not start a hold-down.
        List<DnsResourceRecord> rrset =
        [
            keyA.DnsKeyRecord,
            keyBRevoked.DnsKeyRecord,
            newcomer.DnsKeyRecord,
        ];
        harness.Result = Success(rrset, fixture.Sign(keyBRevoked, DnsName.Root, rrset));

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(
            TrustAnchorStateEnum.Revoked,
            Find(harness, fixture.BuildTrustAnchor(DnsName.Root, keyB)).State
        );
        Assert.Equal(
            TrustAnchorStateEnum.Valid,
            Find(harness, fixture.BuildTrustAnchor(DnsName.Root, keyA)).State
        );
        Assert.Equal(2, harness.Store.Current.Count);
    }

    [Fact]
    public async Task TurningTheUpdateOnAtRuntime_StartsCheckingWithinAnHourWithoutARestart()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        RecursiveServerOptions options = new() { EnableAutomaticTrustAnchorUpdate = false };
        await using TrustAnchorRolloverHarness harness = new(
            [Anchor(fixture, keyA, TrustAnchorStateEnum.Valid)],
            options
        );
        harness.Result = Success(
            [keyA.DnsKeyRecord],
            fixture.Sign(keyA, DnsName.Root, [keyA.DnsKeyRecord])
        );

        await harness.AdvanceAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(0, harness.Checks);

        // The options object is shared with the service's monitor, as a settings PATCH changes it.
        options.EnableAutomaticTrustAnchorUpdate = true;
        await harness.AdvanceAsync(TimeSpan.FromHours(1));

        Assert.Equal(1, harness.Checks);
    }
}
