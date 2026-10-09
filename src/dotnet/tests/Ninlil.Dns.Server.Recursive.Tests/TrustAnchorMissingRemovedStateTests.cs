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
///     This class tests the <see cref="TrustAnchorStateEnum.Missing" /> and
///     <see cref="TrustAnchorStateEnum.Removed" /> states of <see cref="TrustAnchorRolloverService" />,
///     one check at a time, with a signed root DNSKEY RRset from <see cref="DnssecTestFixture" />
///     (the same setup as <see cref="TrustAnchorRolloverServiceEventTests" />).
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
    ///     This method runs one <c>TrustAnchorRolloverService</c> check against a canned result
    ///     and returns the saved state. It waits for <see cref="FakeTrustAnchorStore.LastSaved" /> to
    ///     change from <paramref name="initialAnchors" />, since every check saves once, rather than
    ///     for an event: re-admitting a Removed key as AddPend publishes none.
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

        // The clock starts at the real time, because the fixture's signatures are valid around it.
        ManualTimeProvider clock = new(DateTimeOffset.UtcNow);

        TrustAnchorRolloverService service = new(
            new FakeIterativeResolutionEngine(canned),
            store,
            new StaticOptionsMonitor<RecursiveServerOptions>(options),
            NullLogger<TrustAnchorRolloverService>.Instance,
            publisher,
            clock
        );

        await service.StartAsync(CancellationToken.None);

        // The first check runs one minute after start. The host may start ExecuteAsync on another
        // thread, so wait until it has armed that wait before moving the clock.
        for (int attempt = 0; attempt < 100 && clock.PendingTimerCount == 0; attempt++)
            await Task.Delay(50);

        clock.Advance(TimeSpan.FromMinutes(1));

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

        // keyB is Valid, but this check's DNSKEY RRset carries only keyA.
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
            TrustAnchorRemoveHoldDownDuration = TimeSpan.FromDays(30),
        };

        (IReadOnlyList<PersistedTrustAnchor> saved, RecordingEventPublisher publisher) =
            await RunOneCheckAsync(canned, [validAnchorA, missingCandidateB], options);

        PersistedTrustAnchor savedB = saved.Single(anchor => anchor.KeyTag == keyBShape.KeyTag);
        Assert.Equal(TrustAnchorStateEnum.Missing, savedB.State);
        Assert.NotNull(savedB.MissingSinceUtc);

        // keyA is untouched: still present, still Valid.
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

        // This check's RRset carries both keys again: keyB reappeared before its remove
        // hold-down elapsed.
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
            // Still inside the remove hold-down window.
            DateTimeOffset.UtcNow.AddDays(-1)
        );

        RecursiveServerOptions options = new()
        {
            EnableAutomaticTrustAnchorUpdate = true,
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

        // keyB is absent again and has been Missing longer than the remove hold-down.
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
            // Well past the remove hold-down duration.
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
        // Only a key with the SEP (KSK) bit set is treated as a trust point candidate (RFC 5011),
        // so keyB needs it to be re-admitted as AddPend.
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

        RecursiveServerOptions options = new() { EnableAutomaticTrustAnchorUpdate = true };

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
