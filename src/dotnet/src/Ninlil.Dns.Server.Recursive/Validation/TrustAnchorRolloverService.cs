using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class implements RFC 5011's automated trust anchor rollover: periodically checking
///     the root zone's own DNSKEY RRset for a new candidate key or a self-revoked one, and
///     updating <see cref="ITrustAnchorStore" /> accordingly -- so a deployment left running for
///     years (the homelab/home router use case this was specifically added for) keeps
///     validating correctly straight through a root KSK rollover with no operator intervention,
///     rather than either breaking or silently falling back to unauthenticated answers once its
///     originally-hardcoded anchor is eventually retired.
/// </summary>
/// <param name="engine">The engine this service resolves the root's own DNSKEY RRset through.</param>
/// <param name="store">The trust anchor store this service reads from and updates.</param>
/// <param name="options">
///     This service's own configured refresh interval and hold-down duration, read live via
///     <see cref="IOptionsMonitor{TOptions}" /> -- see <see cref="ExecuteAsync" />'s own doc
///     comment for why this background service's own enable check is re-evaluated every loop
///     iteration rather than once at entry.
/// </param>
/// <param name="logger">The logger this service records every state transition, and every failed check, to.</param>
/// <param name="eventPublisher">
///     The publisher this service raises <see cref="TrustAnchorRolloverEvent" /> through
///     whenever an anchor's own RFC 5011 state actually changes (promoted to <c>Valid</c>, or
///     revoked) -- optional, defaulting to <see langword="null" />, the same pattern
///     <c>SignedZoneCache</c>'s/<c>AcmeCertificateRenewalService</c>'s/
///     <c>CloudDnsZoneSyncService</c>'s own new <c>eventPublisher</c> parameters follow, so every
///     pre-existing caller/test keeps compiling and behaving unchanged.
/// </param>
public sealed class TrustAnchorRolloverService(
    IIterativeResolutionEngine engine,
    ITrustAnchorStore store,
    IOptionsMonitor<RecursiveServerOptions> options,
    ILogger<TrustAnchorRolloverService> logger,
    IDnsServerEventPublisher eventPublisher = null
) : BackgroundService
{
    /// <summary>
    ///     This method runs this service's own real check loop for as long as this host is
    ///     running. Unlike a <see langword="return" />-once-and-exit gate checked only before
    ///     entering the loop, <see cref="RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate" />
    ///     is re-read fresh (via <c>options.CurrentValue</c>) on every iteration, so a live
    ///     <c>RecursiveSettingsController.Patch</c> write toggling it takes effect on this same
    ///     running instance -- including turning it back on after this service was started with
    ///     it off, which an early <see langword="return" /> could never recover from since
    ///     <see cref="BackgroundService.ExecuteAsync" /> only ever runs once per process.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.CurrentValue.TrustAnchorRefreshInterval, stoppingToken);

                if (options.CurrentValue.EnableAutomaticTrustAnchorUpdate)
                    await RunOneCheckAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
            catch (Exception exception)
            {
                // A single failed check (the root unreachable, a transient network issue) must
                // never take this whole background service down -- there is always another
                // check, at worst RefreshInterval later.
                logger.LogWarning(exception, "A trust anchor rollover check failed unexpectedly.");
            }
        }
    }

    /// <summary>
    ///     This method runs one complete RFC 5011 check: fetching the root's own DNSKEY RRset,
    ///     confirming it is validly signed by a key this resolver already trusts, and only then
    ///     running the actual RFC 5011 §4.2 state machine (add/valid/missing/revoke/removed)
    ///     against it.
    /// </summary>
    private async Task RunOneCheckAsync(CancellationToken stoppingToken)
    {
        QueryBudget budget = new(options.CurrentValue);
        IterativeResolutionResult result = await engine.ResolveAsync(
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            true,
            budget,
            forceRootStart: true,
            clientSubnetOption: null,
            stoppingToken: stoppingToken
        );

        if (result.Status != RecursiveResolutionStatusEnum.Success || result.Answer.Count == 0)
        {
            logger.LogWarning(
                "A trust anchor rollover check could not retrieve the root zone's own DNSKEY RRset (status: {Status}).",
                result.Status
            );
            return;
        }

        List<PersistedTrustAnchor> currentAnchors = [.. store.GetAll()];

        // RFC 5011 §5.1: an update is only ever trusted if the RRset it arrived in is itself
        // validly signed by a key that is *already* Valid -- found here by normalizing away
        // each candidate's own Revoke bit before matching, since a self-revoking key's key tag
        // and digest both change the moment that bit is set (RFC 5011 §5.5), and would
        // otherwise never match the anchor it was originally trusted as.
        List<(
            DnsResourceRecord Record,
            DnsKeyRecordData Data,
            PersistedTrustAnchor MatchedAnchor
        )> recognized =
        [
            .. result
                .Answer.Where(record => record.Type == DnsResourceRecordTypeEnum.DnsKey)
                .Select(record => (record, Data: DecodeDnsKey(record)))
                .Where(pair => pair.Data is not null)
                .Select(pair =>
                    (
                        pair.record,
                        pair.Data,
                        MatchedAnchor: FindMatchingAnchor(pair.record, pair.Data, currentAnchors)
                    )
                ),
        ];

        List<DnsResourceRecord> recognizedKeyRecords =
        [
            .. recognized
                .Where(entry => entry.MatchedAnchor is not null)
                .Select(entry => entry.Record),
        ];

        if (
            recognizedKeyRecords.Count == 0
            || !RrsetSignatureVerifier.TryVerify(
                result.Answer,
                result.DnssecRecords,
                recognizedKeyRecords,
                DateTimeOffset.UtcNow
            )
        )
        {
            logger.LogWarning(
                "A trust anchor rollover check's root DNSKEY RRset did not verify against any already-trusted anchor; ignoring it for this check."
            );
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Dictionary<(ushort, DnssecAlgorithmEnum, byte), PersistedTrustAnchor> updated =
            currentAnchors.ToDictionary(anchor =>
                (anchor.KeyTag, anchor.Algorithm, anchor.DigestType)
            );

        foreach (
            (
                DnsResourceRecord record,
                DnsKeyRecordData data,
                PersistedTrustAnchor matchedAnchor
            ) in recognized
        )
        {
            if (
                data.IsRevoked
                && matchedAnchor
                    is { State: TrustAnchorStateEnum.Valid or TrustAnchorStateEnum.Missing }
            )
            {
                logger.LogWarning(
                    "Trust anchor with key tag {KeyTag} has announced its own revocation; no longer trusting it.",
                    matchedAnchor.KeyTag
                );
                updated[(matchedAnchor.KeyTag, matchedAnchor.Algorithm, matchedAnchor.DigestType)] =
                    matchedAnchor with
                    {
                        State = TrustAnchorStateEnum.Revoked,
                    };

                eventPublisher?.Publish(
                    new TrustAnchorRolloverEvent(
                        DateTimeOffset.UtcNow,
                        matchedAnchor.KeyTag,
                        nameof(TrustAnchorStateEnum.Revoked)
                    )
                );
                continue;
            }

            if (data.IsRevoked || !data.IsSecureEntryPoint)
                continue;

            // A previously-Removed candidate reappearing is treated exactly like a brand-new one
            // (RFC 5011 does not forbid re-trusting a genuinely-retired-then-returned key through
            // the ordinary hold-down process again) -- every other already-tracked state
            // (AddPend/Valid/Missing/Revoked) is handled by its own dedicated loop below instead.
            if (matchedAnchor is not null && matchedAnchor.State != TrustAnchorStateEnum.Removed)
                continue;

            // A brand-new (or previously-Removed, now-returned) candidate -- start its hold-down
            // clock from scratch.
            (ushort keyTag, byte digestType, byte[] digest) = ComputeIdentity(record, data);
            updated[(keyTag, data.Algorithm, digestType)] = new PersistedTrustAnchor(
                keyTag,
                data.Algorithm,
                digestType,
                digest,
                TrustAnchorStateEnum.AddPend,
                now,
                null
            );
            logger.LogInformation(
                "Observed a new candidate trust anchor (key tag {KeyTag}); its {HoldDown} hold-down period starts now.",
                keyTag,
                options.CurrentValue.TrustAnchorHoldDownDuration
            );
        }

        // Any AddPend candidate this check did *not* see at all must restart its hold-down
        // clock from scratch the next time it reappears, per RFC 5011 §2.3's requirement that
        // the hold-down period be continuous, uninterrupted observation.
        foreach (PersistedTrustAnchor anchor in currentAnchors)
        {
            if (anchor.State != TrustAnchorStateEnum.AddPend)
                continue;

            (ushort, DnssecAlgorithmEnum, byte) key = (
                anchor.KeyTag,
                anchor.Algorithm,
                anchor.DigestType
            );
            (
                DnsResourceRecord Record,
                DnsKeyRecordData Data,
                PersistedTrustAnchor MatchedAnchor
            ) matchingEntry = recognized.FirstOrDefault(entry =>
                entry.MatchedAnchor is not null
                && entry.MatchedAnchor.KeyTag == anchor.KeyTag
                && entry.MatchedAnchor.Algorithm == anchor.Algorithm
                && entry.MatchedAnchor.DigestType == anchor.DigestType
            );

            if (matchingEntry.Record is null || matchingEntry.Data.IsRevoked)
            {
                // Not seen at all this check, or seen only because it revoked itself before ever
                // completing its hold-down -- either way, this candidate never becomes Valid;
                // drop it from tracking rather than let a stale timer eventually promote it.
                updated.Remove(key);
                continue;
            }

            if (now - anchor.FirstObservedUtc >= options.CurrentValue.TrustAnchorHoldDownDuration)
            {
                updated[key] = anchor with
                {
                    State = TrustAnchorStateEnum.Valid,
                    ValidSinceUtc = now,
                };
                logger.LogInformation(
                    "Promoted trust anchor with key tag {KeyTag} to Valid after its hold-down period elapsed.",
                    anchor.KeyTag
                );

                eventPublisher?.Publish(
                    new TrustAnchorRolloverEvent(
                        DateTimeOffset.UtcNow,
                        anchor.KeyTag,
                        nameof(TrustAnchorStateEnum.Valid)
                    )
                );
            }
        }

        // RFC 5011 §4.2: a currently Valid anchor that this check's own DNSKEY RRset did not
        // include at all -- and which the revocation branch above did not already handle --
        // transitions to Missing rather than either staying Valid forever or being dropped
        // immediately. A genuine permanent retirement and a transient outage/partial-response
        // glitch look identical on any single check; the remove hold-down clock started here,
        // and resolved by the loop just below, is what actually tells them apart over time.
        foreach (
            PersistedTrustAnchor anchor in currentAnchors.Where(a =>
                a.State == TrustAnchorStateEnum.Valid
            )
        )
        {
            (ushort, DnssecAlgorithmEnum, byte) key = (
                anchor.KeyTag,
                anchor.Algorithm,
                anchor.DigestType
            );

            // Already handled this same check by the per-record revocation branch above.
            if (
                updated.TryGetValue(key, out PersistedTrustAnchor alreadyUpdated)
                && alreadyUpdated.State == TrustAnchorStateEnum.Revoked
            )
                continue;

            bool stillPresent = recognized.Any(entry =>
                entry.MatchedAnchor?.KeyTag == anchor.KeyTag
            );

            if (stillPresent)
                continue;

            updated[key] = anchor with
            {
                State = TrustAnchorStateEnum.Missing,
                MissingSinceUtc = now,
            };
            logger.LogWarning(
                "Trust anchor with key tag {KeyTag} was not present in this check's own DNSKEY RRset; marking it Missing and starting its {HoldDown} remove hold-down clock.",
                anchor.KeyTag,
                options.CurrentValue.TrustAnchorRemoveHoldDownDuration
            );

            eventPublisher?.Publish(
                new TrustAnchorRolloverEvent(
                    DateTimeOffset.UtcNow,
                    anchor.KeyTag,
                    nameof(TrustAnchorStateEnum.Missing)
                )
            );
        }

        // The other half of RFC 5011 §4.2's Missing state: an anchor already Missing either
        // reappears (returned directly to Valid, its clock cleared) or, having stayed Missing
        // through the full remove hold-down duration with no reappearance, is retired to Removed.
        foreach (
            PersistedTrustAnchor anchor in currentAnchors.Where(a =>
                a.State == TrustAnchorStateEnum.Missing
            )
        )
        {
            (ushort, DnssecAlgorithmEnum, byte) key = (
                anchor.KeyTag,
                anchor.Algorithm,
                anchor.DigestType
            );

            // Already handled this same check by the per-record revocation branch above (a
            // Missing anchor can announce its own revocation just as a Valid one can).
            if (
                updated.TryGetValue(key, out PersistedTrustAnchor alreadyUpdated)
                && alreadyUpdated.State == TrustAnchorStateEnum.Revoked
            )
                continue;

            bool reappeared = recognized.Any(entry => entry.MatchedAnchor?.KeyTag == anchor.KeyTag);

            if (reappeared)
            {
                updated[key] = anchor with
                {
                    State = TrustAnchorStateEnum.Valid,
                    MissingSinceUtc = null,
                };
                logger.LogInformation(
                    "Trust anchor with key tag {KeyTag} reappeared before its remove hold-down elapsed; returning it to Valid.",
                    anchor.KeyTag
                );

                eventPublisher?.Publish(
                    new TrustAnchorRolloverEvent(
                        DateTimeOffset.UtcNow,
                        anchor.KeyTag,
                        nameof(TrustAnchorStateEnum.Valid)
                    )
                );
                continue;
            }

            if (
                anchor.MissingSinceUtc is null
                || now - anchor.MissingSinceUtc.Value
                    < options.CurrentValue.TrustAnchorRemoveHoldDownDuration
            )
                continue;

            updated[key] = anchor with
            {
                State = TrustAnchorStateEnum.Removed,
                MissingSinceUtc = null,
            };
            logger.LogWarning(
                "Trust anchor with key tag {KeyTag} stayed Missing through its full remove hold-down period without reappearing; it is now Removed.",
                anchor.KeyTag
            );

            eventPublisher?.Publish(
                new TrustAnchorRolloverEvent(
                    DateTimeOffset.UtcNow,
                    anchor.KeyTag,
                    nameof(TrustAnchorStateEnum.Removed)
                )
            );
        }

        store.Save([.. updated.Values]);
    }

    /// <summary>
    ///     This method decodes a DNSKEY record, returning <see langword="null" /> for anything
    ///     that fails to decode rather than throwing -- a single malformed record in an
    ///     otherwise-valid response should not take this entire check down.
    /// </summary>
    private static DnsKeyRecordData DecodeDnsKey(DnsResourceRecord record) =>
        DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DnsKey, record.Data)
        as DnsKeyRecordData;

    /// <summary>
    ///     This method computes a DNSKEY's key tag and DS-form digest, always with its own
    ///     Revoke bit cleared first -- the identity a persisted trust anchor is tracked and
    ///     matched by, since revoking a key changes both its key tag and its digest (RFC 5011
    ///     §5.5), but must still resolve back to the *same* tracked candidate it always was.
    /// </summary>
    private static (ushort KeyTag, byte DigestType, byte[] Digest) ComputeIdentity(
        DnsResourceRecord record,
        DnsKeyRecordData data
    )
    {
        const byte digestType = 2;

        DnsKeyRecordData normalized = new(
            data.IsZoneKey,
            data.IsSecureEntryPoint,
            data.Algorithm,
            data.PublicKey
        );
        byte[] normalizedRdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            normalized
        );
        ushort keyTag = DnssecKeyTag.Compute(data.Algorithm, normalizedRdata);
        byte[] digest = DsDigestCodec.Compute(digestType, record.Name, normalizedRdata);

        return (keyTag, digestType, digest);
    }

    /// <summary>
    ///     This method finds whichever already-tracked anchor a DNSKEY record's normalized
    ///     identity matches, if any.
    /// </summary>
    private static PersistedTrustAnchor FindMatchingAnchor(
        DnsResourceRecord record,
        DnsKeyRecordData data,
        IReadOnlyList<PersistedTrustAnchor> anchors
    )
    {
        (ushort keyTag, byte digestType, byte[] digest) = ComputeIdentity(record, data);

        return anchors.FirstOrDefault(anchor =>
            anchor.KeyTag == keyTag
            && anchor.Algorithm == data.Algorithm
            && anchor.DigestType == digestType
            && anchor.Digest.AsSpan().SequenceEqual(digest)
        );
    }
}
