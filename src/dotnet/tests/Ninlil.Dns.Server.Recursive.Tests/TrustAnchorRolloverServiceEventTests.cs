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
///     This class checks that <see cref="TrustAnchorRolloverService" /> publishes
///     <see cref="TrustAnchorRolloverEvent" /> when a candidate completes its hold-down and becomes
///     <c>Valid</c>. The service runs as a started background service with a short refresh
///     interval, a fake engine returning a signed root DNSKEY RRset from
///     <see cref="DnssecTestFixture" />, and an in-memory store.
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

        // keyA is the Valid root anchor; keyB is an AddPend candidate seen on an earlier check
        // whose hold-down has now elapsed.
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
            // Short, because ExecuteAsync waits this long before its first check.
            TrustAnchorRefreshInterval = TimeSpan.FromMilliseconds(50),
        };

        PersistedTrustAnchor pendingAnchor = new(
            pendingAnchorShape.KeyTag,
            pendingAnchorShape.Algorithm,
            pendingAnchorShape.DigestType,
            pendingAnchorShape.Digest,
            TrustAnchorStateEnum.AddPend,
            // Well past the hold-down duration configured above.
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

        // StartAsync returns once ExecuteAsync reaches its first await (the refresh delay), so
        // poll for the event rather than assuming the check has run.
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
