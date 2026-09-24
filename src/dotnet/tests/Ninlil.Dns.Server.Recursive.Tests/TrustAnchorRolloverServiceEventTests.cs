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
///     This class covers <see cref="TrustAnchorRolloverEvent" />, confirming it actually fires
///     from <see cref="TrustAnchorRolloverService" />'s own real trigger point
///     -- a genuine RFC 5011 promotion-to-<c>Valid</c> state transition, computed by that
///     service's real state machine against a real, cryptographically valid, signed root DNSKEY
///     RRset (via <see cref="DnssecTestFixture" />), not a stubbed-out result. This is this
///     project's first permanent test of <see cref="TrustAnchorRolloverService" /> itself; every
///     prior verification of it was a smoke test only.
/// </summary>
public sealed class TrustAnchorRolloverServiceEventTests
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

    [Fact]
    public async Task ACandidateCompletingItsHoldDownPublishesTheRolloverEventAsValid()
    {
        DnssecTestFixture fixture = new();

        // keyA is the currently Valid, active root anchor; keyB is a candidate this resolver
        // already observed on a previous check (AddPend) whose hold-down period has, as of now,
        // already elapsed.
        DnssecTestFixture.ZoneKey keyA = fixture.GenerateZoneKey(DnsName.Root);
        DnssecTestFixture.ZoneKey keyB = fixture.GenerateZoneKey(DnsName.Root);

        DnsResourceRecord rrsig = fixture.Sign(
            keyA,
            DnsName.Root,
            [keyA.DnsKeyRecord, keyB.DnsKeyRecord]
        );

        IterativeResolutionResult canned = new(
            RecursiveResolutionStatusEnum.Success,
            [keyA.DnsKeyRecord, keyB.DnsKeyRecord],
            [rrsig],
            null,
            []
        );

        TrustAnchor validAnchorShape = fixture.BuildTrustAnchor(DnsName.Root, keyA);
        TrustAnchor pendingAnchorShape = fixture.BuildTrustAnchor(DnsName.Root, keyB);

        PersistedTrustAnchor validAnchor = new(
            validAnchorShape.KeyTag,
            validAnchorShape.Algorithm,
            validAnchorShape.DigestType,
            validAnchorShape.Digest,
            TrustAnchorStateEnum.Valid,
            DateTimeOffset.UtcNow.AddDays(-400),
            DateTimeOffset.UtcNow.AddDays(-370)
        );

        RecursiveServerOptions options = new()
        {
            EnableAutomaticTrustAnchorUpdate = true,
            TrustAnchorHoldDownDuration = TimeSpan.FromDays(30),
            // Deliberately tiny -- ExecuteAsync awaits this delay *before* its first check
            // (unlike CloudDnsZoneSyncService's/AcmeCertificateRenewalService's own loops, which
            // run their real work before their first delay), so this needs to be short enough for
            // a real StartAsync-then-poll test to observe the check's own side effect quickly.
            TrustAnchorRefreshInterval = TimeSpan.FromMilliseconds(50),
        };

        PersistedTrustAnchor pendingAnchor = new(
            pendingAnchorShape.KeyTag,
            pendingAnchorShape.Algorithm,
            pendingAnchorShape.DigestType,
            pendingAnchorShape.Digest,
            TrustAnchorStateEnum.AddPend,
            // Comfortably past the hold-down duration configured above.
            DateTimeOffset.UtcNow
                - options.TrustAnchorHoldDownDuration
                - TimeSpan.FromDays(1),
            null
        );

        FakeTrustAnchorStore store = new([validAnchor, pendingAnchor]);
        RecordingEventPublisher publisher = new();

        TrustAnchorRolloverService service = new(
            new FakeIterativeResolutionEngine(canned),
            store,
            new StaticOptionsMonitor<RecursiveServerOptions>(options),
            NullLogger<TrustAnchorRolloverService>.Instance,
            publisher
        );

        // TrustAnchorRolloverService.ExecuteAsync runs its own real check loop as a
        // BackgroundService; StartAsync returns once ExecuteAsync hits its first genuine await
        // (the configured refresh interval, set deliberately tiny above), the same real,
        // empirically-confirmed scheduling gap CloudDnsZoneSyncServiceTests'/
        // AcmeCertificateRenewalServiceEventTests' own StartAsync-then-poll pattern already
        // establishes for two different background services -- poll for the expected side
        // effect instead of assuming it has already happened.
        await service.StartAsync(CancellationToken.None);

        for (int attempt = 0; attempt < 100 && publisher.Published.Count == 0; attempt++)
            await Task.Delay(50);

        await service.StopAsync(CancellationToken.None);

        TrustAnchorRolloverEvent rolloverEvent = Assert.Single(
            publisher.Published.OfType<TrustAnchorRolloverEvent>()
        );
        Assert.Equal(pendingAnchorShape.KeyTag, rolloverEvent.KeyTag);
        Assert.Equal(nameof(TrustAnchorStateEnum.Valid), rolloverEvent.NewState);

        PersistedTrustAnchor savedPendingAnchor = store.LastSaved.Single(anchor =>
            anchor.KeyTag == pendingAnchorShape.KeyTag
        );
        Assert.Equal(TrustAnchorStateEnum.Valid, savedPendingAnchor.State);
    }
}
