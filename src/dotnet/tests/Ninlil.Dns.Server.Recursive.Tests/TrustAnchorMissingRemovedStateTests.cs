using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Tests.Support;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class covers RFC 5011 §4.2's <see cref="TrustAnchorStateEnum.Missing" />/
///     <see cref="TrustAnchorStateEnum.Removed" /> states end to end -- a previously-untested
///     part of <see cref="TrustAnchorRolloverService" />'s own real state machine, using the same
///     real, cryptographically-signed root DNSKEY RRset (via <see cref="DnssecTestFixture" />)
///     and single-check-at-a-time invocation style <see cref="TrustAnchorRolloverServiceEventTests" />
///     already established.
/// </summary>
public sealed class TrustAnchorMissingRemovedStateTests
{
    private sealed class FakeIterativeResolutionEngine(IterativeResolutionResult canned)
        : IIterativeResolutionEngine
    {
        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(canned);

        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            QueryBudget budget,
            bool forceRootStart,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(canned);

        public Task<IterativeResolutionResult> PrefetchAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(canned);
    }

    private sealed class FakeTrustAnchorStore(List<PersistedTrustAnchor> initial)
        : ITrustAnchorStore
    {
        public IReadOnlyList<PersistedTrustAnchor> LastSaved { get; private set; } = initial;

        public IReadOnlyList<TrustAnchor> GetValidAnchors() =>
            [
                .. LastSaved
                    .Where(anchor => anchor.State == TrustAnchorStateEnum.Valid)
                    .Select(anchor => new TrustAnchor(
                        anchor.KeyTag,
                        anchor.Algorithm,
                        anchor.DigestType,
                        anchor.Digest
                    )),
            ];

        public IReadOnlyList<PersistedTrustAnchor> GetAll() => LastSaved;

        public void Save(IReadOnlyList<PersistedTrustAnchor> anchors) => LastSaved = anchors;
    }

    /// <summary>
    ///     Runs exactly one <c>TrustAnchorRolloverService</c> check against a canned resolution
    ///     result and returns the store's own state afterward. Polls for
    ///     <see cref="FakeTrustAnchorStore.LastSaved" /> changing reference identity from
    ///     <paramref name="initialAnchors" /> as its completion signal (the service's own
    ///     <c>RunOneCheckAsync</c> always calls <c>store.Save(...)</c> exactly once per check,
    ///     replacing that reference, regardless of whether any anchor's own state actually
    ///     changed) rather than <see cref="TrustAnchorRolloverServiceEventTests" />'s own
    ///     publisher-fired signal, since a check that only promotes a Removed anchor back to a
    ///     fresh AddPend candidate publishes no event at all.
    /// </summary>
    private static async Task<(
        IReadOnlyList<PersistedTrustAnchor> Saved,
        RecordingEventPublisher Publisher
    )> RunOneCheckAsync(
        IterativeResolutionResult canned,
        List<PersistedTrustAnchor> initialAnchors,
        RecursiveServerOptions options
    )
    {
        FakeTrustAnchorStore store = new(initialAnchors);
        RecordingEventPublisher publisher = new();

        TrustAnchorRolloverService service = new(
            new FakeIterativeResolutionEngine(canned),
            store,
            new StaticOptionsMonitor<RecursiveServerOptions>(options),
            NullLogger<TrustAnchorRolloverService>.Instance,
            publisher
        );

        await service.StartAsync(CancellationToken.None);

        for (
            int attempt = 0;
            attempt < 100 && ReferenceEquals(store.LastSaved, initialAnchors);
            attempt++
        )
            await Task.Delay(50);

        await service.StopAsync(CancellationToken.None);

        return (store.LastSaved, publisher);
    }

    [Fact]
    public async Task AValidAnchorAbsentFromTheDnsKeyRrsetTransitionsToMissing()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        DnsResourceRecord rrsig = fixture.Sign(keyA, DnsName.Root, [keyA.DnsKeyRecord]);

        // keyB is Valid and already trusted, but this check's own DNSKEY RRset carries only
        // keyA -- keyB has genuinely disappeared from the target zone's own answer.
        TrustAnchor keyAShape = fixture.BuildTrustAnchor(DnsName.Root, keyA);
        DnssecTestFixture.ZoneKey keyB = fixture.GenerateZoneKey(DnsName.Root);
        TrustAnchor keyBShape = fixture.BuildTrustAnchor(DnsName.Root, keyB);

        IterativeResolutionResult canned = new(
            RecursiveResolutionStatusEnum.Success,
            [keyA.DnsKeyRecord],
            [rrsig],
            null,
            []
        );

        PersistedTrustAnchor validAnchorA = new(
            keyAShape.KeyTag,
            keyAShape.Algorithm,
            keyAShape.DigestType,
            keyAShape.Digest,
            TrustAnchorStateEnum.Valid,
            DateTimeOffset.UtcNow.AddDays(-400),
            DateTimeOffset.UtcNow.AddDays(-370)
        );
        PersistedTrustAnchor missingCandidateB = new(
            keyBShape.KeyTag,
            keyBShape.Algorithm,
            keyBShape.DigestType,
            keyBShape.Digest,
            TrustAnchorStateEnum.Valid,
            DateTimeOffset.UtcNow.AddDays(-400),
            DateTimeOffset.UtcNow.AddDays(-370)
        );

        RecursiveServerOptions options = new()
        {
            EnableAutomaticTrustAnchorUpdate = true,
            TrustAnchorRefreshInterval = TimeSpan.FromMilliseconds(50),
            TrustAnchorRemoveHoldDownDuration = TimeSpan.FromDays(30),
        };

        (IReadOnlyList<PersistedTrustAnchor> saved, RecordingEventPublisher publisher) =
            await RunOneCheckAsync(canned, [validAnchorA, missingCandidateB], options);

        PersistedTrustAnchor savedB = saved.Single(anchor => anchor.KeyTag == keyBShape.KeyTag);
        Assert.Equal(TrustAnchorStateEnum.Missing, savedB.State);
        Assert.NotNull(savedB.MissingSinceUtc);

        // keyA is untouched -- still present, still Valid.
        PersistedTrustAnchor savedA = saved.Single(anchor => anchor.KeyTag == keyAShape.KeyTag);
        Assert.Equal(TrustAnchorStateEnum.Valid, savedA.State);

        TrustAnchorRolloverEvent rolloverEvent = Assert.Single(
            publisher.Published.OfType<TrustAnchorRolloverEvent>()
        );
        Assert.Equal(keyBShape.KeyTag, rolloverEvent.KeyTag);
        Assert.Equal(nameof(TrustAnchorStateEnum.Missing), rolloverEvent.NewState);
    }

    [Fact]
    public async Task AMissingAnchorThatReappearsReturnsDirectlyToValid()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey keyB = fixture.GenerateZoneKey(DnsName.Root);
        DnsResourceRecord rrsig = fixture.Sign(
            keyA,
            DnsName.Root,
            [keyA.DnsKeyRecord, keyB.DnsKeyRecord]
        );

        TrustAnchor keyAShape = fixture.BuildTrustAnchor(DnsName.Root, keyA);
        TrustAnchor keyBShape = fixture.BuildTrustAnchor(DnsName.Root, keyB);

        // This check's own DNSKEY RRset carries both keys again -- keyB, previously Missing, has
        // reappeared before its remove hold-down elapsed.
        IterativeResolutionResult canned = new(
            RecursiveResolutionStatusEnum.Success,
            [keyA.DnsKeyRecord, keyB.DnsKeyRecord],
            [rrsig],
            null,
            []
        );

        PersistedTrustAnchor validAnchorA = new(
            keyAShape.KeyTag,
            keyAShape.Algorithm,
            keyAShape.DigestType,
            keyAShape.Digest,
            TrustAnchorStateEnum.Valid,
            DateTimeOffset.UtcNow.AddDays(-400),
            DateTimeOffset.UtcNow.AddDays(-370)
        );
        PersistedTrustAnchor missingAnchorB = new(
            keyBShape.KeyTag,
            keyBShape.Algorithm,
            keyBShape.DigestType,
            keyBShape.Digest,
            TrustAnchorStateEnum.Missing,
            DateTimeOffset.UtcNow.AddDays(-400),
            DateTimeOffset.UtcNow.AddDays(-370),
            // Comfortably within the remove hold-down window still.
            DateTimeOffset.UtcNow.AddDays(-1)
        );

        RecursiveServerOptions options = new()
        {
            EnableAutomaticTrustAnchorUpdate = true,
            TrustAnchorRefreshInterval = TimeSpan.FromMilliseconds(50),
            TrustAnchorRemoveHoldDownDuration = TimeSpan.FromDays(30),
        };

        (IReadOnlyList<PersistedTrustAnchor> saved, RecordingEventPublisher publisher) =
            await RunOneCheckAsync(canned, [validAnchorA, missingAnchorB], options);

        PersistedTrustAnchor savedB = saved.Single(anchor => anchor.KeyTag == keyBShape.KeyTag);
        Assert.Equal(TrustAnchorStateEnum.Valid, savedB.State);
        Assert.Null(savedB.MissingSinceUtc);

        TrustAnchorRolloverEvent rolloverEvent = Assert.Single(
            publisher.Published.OfType<TrustAnchorRolloverEvent>()
        );
        Assert.Equal(keyBShape.KeyTag, rolloverEvent.KeyTag);
        Assert.Equal(nameof(TrustAnchorStateEnum.Valid), rolloverEvent.NewState);
    }

    [Fact]
    public async Task AMissingAnchorThatNeverReappearsIsRemovedOnceItsHoldDownElapses()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        DnsResourceRecord rrsig = fixture.Sign(keyA, DnsName.Root, [keyA.DnsKeyRecord]);

        TrustAnchor keyAShape = fixture.BuildTrustAnchor(DnsName.Root, keyA);
        DnssecTestFixture.ZoneKey keyB = fixture.GenerateZoneKey(DnsName.Root);
        TrustAnchor keyBShape = fixture.BuildTrustAnchor(DnsName.Root, keyB);

        // keyB is absent again this check too -- it has now been Missing for longer than the
        // configured remove hold-down duration, with no reappearance.
        IterativeResolutionResult canned = new(
            RecursiveResolutionStatusEnum.Success,
            [keyA.DnsKeyRecord],
            [rrsig],
            null,
            []
        );

        PersistedTrustAnchor validAnchorA = new(
            keyAShape.KeyTag,
            keyAShape.Algorithm,
            keyAShape.DigestType,
            keyAShape.Digest,
            TrustAnchorStateEnum.Valid,
            DateTimeOffset.UtcNow.AddDays(-400),
            DateTimeOffset.UtcNow.AddDays(-370)
        );
        RecursiveServerOptions options = new()
        {
            EnableAutomaticTrustAnchorUpdate = true,
            TrustAnchorRefreshInterval = TimeSpan.FromMilliseconds(50),
            TrustAnchorRemoveHoldDownDuration = TimeSpan.FromDays(30),
        };
        PersistedTrustAnchor missingAnchorB = new(
            keyBShape.KeyTag,
            keyBShape.Algorithm,
            keyBShape.DigestType,
            keyBShape.Digest,
            TrustAnchorStateEnum.Missing,
            DateTimeOffset.UtcNow.AddDays(-400),
            DateTimeOffset.UtcNow.AddDays(-370),
            // Comfortably past the configured remove hold-down duration.
            DateTimeOffset.UtcNow
                - options.TrustAnchorRemoveHoldDownDuration
                - TimeSpan.FromDays(1)
        );

        (IReadOnlyList<PersistedTrustAnchor> saved, RecordingEventPublisher publisher) =
            await RunOneCheckAsync(canned, [validAnchorA, missingAnchorB], options);

        PersistedTrustAnchor savedB = saved.Single(anchor => anchor.KeyTag == keyBShape.KeyTag);
        Assert.Equal(TrustAnchorStateEnum.Removed, savedB.State);
        Assert.Null(savedB.MissingSinceUtc);

        TrustAnchorRolloverEvent rolloverEvent = Assert.Single(
            publisher.Published.OfType<TrustAnchorRolloverEvent>()
        );
        Assert.Equal(keyBShape.KeyTag, rolloverEvent.KeyTag);
        Assert.Equal(nameof(TrustAnchorStateEnum.Removed), rolloverEvent.NewState);
    }

    [Fact]
    public async Task ARemovedAnchorThatReappearsIsTreatedAsABrandNewCandidate()
    {
        DnssecTestFixture fixture = new();
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        // RFC 5011 §7.2: only a key with its own SEP (secure entry point / KSK) bit set is ever
        // considered a real trust point candidate at all -- required here since this test's own
        // real assertion is about keyB being re-admitted as a brand-new AddPend candidate.
        DnssecTestFixture.ZoneKey keyB = fixture.GenerateZoneKey(
            DnsName.Root,
            isSecureEntryPoint: true
        );
        DnsResourceRecord rrsig = fixture.Sign(
            keyA,
            DnsName.Root,
            [keyA.DnsKeyRecord, keyB.DnsKeyRecord]
        );

        TrustAnchor keyAShape = fixture.BuildTrustAnchor(DnsName.Root, keyA);
        TrustAnchor keyBShape = fixture.BuildTrustAnchor(DnsName.Root, keyB);

        IterativeResolutionResult canned = new(
            RecursiveResolutionStatusEnum.Success,
            [keyA.DnsKeyRecord, keyB.DnsKeyRecord],
            [rrsig],
            null,
            []
        );

        PersistedTrustAnchor validAnchorA = new(
            keyAShape.KeyTag,
            keyAShape.Algorithm,
            keyAShape.DigestType,
            keyAShape.Digest,
            TrustAnchorStateEnum.Valid,
            DateTimeOffset.UtcNow.AddDays(-400),
            DateTimeOffset.UtcNow.AddDays(-370)
        );
        PersistedTrustAnchor removedAnchorB = new(
            keyBShape.KeyTag,
            keyBShape.Algorithm,
            keyBShape.DigestType,
            keyBShape.Digest,
            TrustAnchorStateEnum.Removed,
            DateTimeOffset.UtcNow.AddDays(-400),
            DateTimeOffset.UtcNow.AddDays(-370)
        );

        RecursiveServerOptions options = new()
        {
            EnableAutomaticTrustAnchorUpdate = true,
            TrustAnchorRefreshInterval = TimeSpan.FromMilliseconds(50),
        };

        (IReadOnlyList<PersistedTrustAnchor> saved, _) = await RunOneCheckAsync(
            canned,
            [validAnchorA, removedAnchorB],
            options
        );

        PersistedTrustAnchor savedB = saved.Single(anchor => anchor.KeyTag == keyBShape.KeyTag);
        Assert.Equal(TrustAnchorStateEnum.AddPend, savedB.State);
        Assert.Null(savedB.ValidSinceUtc);
    }
}
