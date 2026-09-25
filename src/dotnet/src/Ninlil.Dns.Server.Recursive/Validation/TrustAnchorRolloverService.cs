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
///     This class implements RFC 5011 automated trust anchor rollover. Every
///     <see cref="RecursiveServerOptions.TrustAnchorRefreshInterval" /> it fetches the root
///     DNSKEY RRset, checks that it is signed by a tracked key, and moves each candidate through
///     the states in <see cref="TrustAnchorStateEnum" />, saving the result in
///     <see cref="ITrustAnchorStore" />. This lets a server left running for years follow a root
///     KSK rollover without an operator. The first check happens one interval after startup,
///     so the wait restarts with every restart.
/// </summary>
/// <param name="engine">The engine this service resolves the root DNSKEY RRset through.</param>
/// <param name="store">The trust anchor store this service reads from and updates.</param>
/// <param name="options">
///     The enable switch, refresh interval and hold-down durations, read live (see
///     <see cref="ExecuteAsync" />).
/// </param>
/// <param name="logger">The logger this service records state changes and failed checks to.</param>
/// <param name="eventPublisher">
///     The publisher for <see cref="TrustAnchorRolloverEvent" />, raised when an anchor becomes
///     Valid, Missing, Revoked or Removed. <see langword="null" /> (the default) publishes
///     nothing.
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
    ///     This method runs the check loop for the life of the host: wait one refresh interval,
    ///     then check if <see cref="RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate" /> is
    ///     on. The flag is read on every iteration rather than once before the loop, because
    ///     <see cref="BackgroundService.ExecuteAsync" /> runs only once per process, so turning it
    ///     on through <c>RecursiveSettingsController.Patch</c> must not need a restart.
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
                // A failed check (root unreachable, a network error) must not stop the service;
                // the next check is one interval later.
                logger.LogWarning(exception, "A trust anchor rollover check failed unexpectedly.");
            }
        }
    }

    /// <summary>
    ///     This method runs one check: fetch the root DNSKEY RRset from the root down, confirm its
    ///     signature against a DNSKEY that matches a tracked anchor, then apply the state
    ///     transitions (add, promote, revoke, missing, removed) and save. The signing key may match
    ///     an anchor in any tracked state, not only <see cref="TrustAnchorStateEnum.Valid" />.
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

        // RFC 5011: an update is only accepted from an RRset signed by a trusted key. Keys are
        // matched with the REVOKE bit cleared, because setting it changes the key tag and
        // digest, and a revoked key must still match the anchor it was trusted as.
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

            // A Removed key that reappears is treated as a new candidate and must pass the
            // hold-down again. Other tracked states are handled by the loops below.
            if (matchedAnchor is not null && matchedAnchor.State != TrustAnchorStateEnum.Removed)
                continue;

            // A new (or returning Removed) candidate: start its hold-down clock.
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

        // An AddPend candidate missing from this check is dropped, so its hold-down restarts if it
        // returns: RFC 5011 requires the key to be seen throughout the hold-down period.
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
                // Not seen, or seen revoked before finishing its hold-down: drop it, so a stale
                // timer can never promote it.
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

        // A Valid anchor absent from this RRset (and not revoked above) becomes Missing. One check
        // cannot tell a retirement from a glitch, so the remove hold-down started here decides
        // (see the next loop). A Missing anchor is no longer used for validation. Presence is
        // matched by key tag only.
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

        // A Missing anchor either reappears (back to Valid) or, after the remove hold-down, is
        // Removed.
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

            // Already revoked above (a Missing anchor can announce revocation too).
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
    ///     This method decodes a DNSKEY record. Malformed RDATA makes the codec throw, which fails
    ///     the whole check (the loop logs it and tries again next interval).
    /// </summary>
    private static DnsKeyRecordData DecodeDnsKey(DnsResourceRecord record) =>
        DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DnsKey, record.Data)
        as DnsKeyRecordData;

    /// <summary>
    ///     This method computes a DNSKEY's key tag and SHA-256 DS digest with the REVOKE bit
    ///     cleared. That is the identity a tracked anchor is matched by, since revoking a key
    ///     changes its key tag and digest but it must still match the same anchor.
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
    ///     This method finds the tracked anchor, in any state, that a DNSKEY's normalized identity
    ///     matches, if any.
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
