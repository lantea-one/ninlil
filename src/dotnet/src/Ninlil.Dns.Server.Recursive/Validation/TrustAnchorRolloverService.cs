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
///     This class implements RFC 5011 automated trust anchor rollover. It fetches the root DNSKEY
///     RRset, accepts it only if a key whose anchor is Valid or Missing signs it (RFC 5011 §4),
///     and moves each candidate through the states in <see cref="TrustAnchorStateEnum" />,
///     saving the result in <see cref="ITrustAnchorStore" />. A revocation counts only when the
///     revoked key signs the RRset itself (RFC 5011 §2.1). This lets a server left running for
///     years follow a root KSK rollover without an operator.
///     <para>
///         The first check runs one minute after startup. After that the wait follows the Active
///         Refresh rule of RFC 5011 §2.3: after a successful check it is
///         MAX(1 hour, MIN(<see cref="RecursiveServerOptions.TrustAnchorRefreshInterval" />, half
///         the DNSKEY signature's Original TTL, half the time left until it expires)); after a
///         failed check it is MAX(1 hour, MIN(1 day, a tenth of the same two values)), or 1 hour
///         while no check has succeeded yet. The configured interval is therefore a maximum.
///     </para>
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
/// <param name="timeProvider">
///     The clock for waits, signature validity and hold-down times. <see langword="null" /> (the
///     default) means <see cref="TimeProvider.System" />.
/// </param>
public sealed class TrustAnchorRolloverService(
    IIterativeResolutionEngine engine,
    ITrustAnchorStore store,
    IOptionsMonitor<RecursiveServerOptions> options,
    ILogger<TrustAnchorRolloverService> logger,
    IDnsServerEventPublisher eventPublisher = null,
    TimeProvider timeProvider = null
) : BackgroundService
{
    /// <summary>
    ///     This field is the wait before the first check, so a server that starts right after a
    ///     rollover does not wait out a full interval.
    /// </summary>
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     This field is the lower bound of every wait (RFC 5011 §2.3), and how often the service
    ///     looks at <see cref="RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate" /> while
    ///     it is off.
    /// </summary>
    private static readonly TimeSpan MinimumWait = TimeSpan.FromHours(1);

    /// <summary>
    ///     This field is the upper bound of the wait after a failed check (RFC 5011 §2.3).
    /// </summary>
    private static readonly TimeSpan MaximumRetryWait = TimeSpan.FromDays(1);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    // The Original TTL and expiration of the DNSKEY signature of the last successful check; null
    // until one succeeds. Only the loop reads or writes them.
    private TimeSpan? lastOriginalTtl;
    private DateTimeOffset lastExpiration;

    /// <summary>
    ///     This method runs the check loop for the life of the host: wait, then check if
    ///     <see cref="RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate" /> is on, then pick
    ///     the next wait from the outcome (see the class summary). The flag is read on every
    ///     iteration rather than once before the loop, because
    ///     <see cref="BackgroundService.ExecuteAsync" /> runs only once per process, so turning it
    ///     on through <c>RecursiveSettingsController.Patch</c> must not need a restart; while it is
    ///     off the loop looks again after at most one hour.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan delay = FirstCheckDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, clock, stoppingToken);

                if (options.CurrentValue.EnableAutomaticTrustAnchorUpdate)
                {
                    bool succeeded = await RunOneCheckAsync(stoppingToken);
                    delay = NextDelay(succeeded);
                }
                else
                {
                    delay = Min(MinimumWait, options.CurrentValue.TrustAnchorRefreshInterval);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during a graceful shutdown.
            }
            catch (Exception exception)
            {
                // A failed check (root unreachable, a network error) must not stop the service;
                // it retries on the failure schedule.
                logger.LogWarning(exception, "A trust anchor rollover check failed unexpectedly.");
                delay = NextDelay(false);
            }
        }
    }

    /// <summary>
    ///     This method computes the wait before the next check from RFC 5011 §2.3, using the
    ///     signature values of the last successful check.
    /// </summary>
    /// <param name="succeeded">Whether the check that just ran succeeded.</param>
    private TimeSpan NextDelay(bool succeeded)
    {
        TimeSpan maximum = options.CurrentValue.TrustAnchorRefreshInterval;

        if (lastOriginalTtl is not { } originalTtl)
            return MinimumWait;

        TimeSpan untilExpiration = lastExpiration - clock.GetUtcNow();

        // An expired signature makes the expiry term zero or negative, which the floor turns into
        // the one hour minimum.
        if (succeeded)
            return Max(MinimumWait, Min(maximum, Min(originalTtl / 2, untilExpiration / 2)));

        return Max(MinimumWait, Min(MaximumRetryWait, Min(originalTtl / 10, untilExpiration / 10)));
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    /// <summary>
    ///     This method runs one check: fetch the root DNSKEY RRset from the root down, accept it
    ///     only if it verifies against a DNSKEY whose anchor is
    ///     <see cref="TrustAnchorStateEnum.Valid" /> or <see cref="TrustAnchorStateEnum.Missing" />
    ///     (RFC 5011 §4), then apply the state transitions (add, promote, revoke, missing,
    ///     removed) and save. A key announces its revocation with the REVOKE bit and counts only
    ///     if that revoked key signs the RRset itself (RFC 5011 §2.1). When no trusted, unrevoked
    ///     key signs the RRset, only such self-signed revocations are applied and the rest of the
    ///     RRset is ignored.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the RRset was retrieved and verified by a trusted key, which
    ///     decides the next wait.
    /// </returns>
    private async Task<bool> RunOneCheckAsync(CancellationToken stoppingToken)
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
            return false;
        }

        List<PersistedTrustAnchor> currentAnchors = [.. store.GetAll()];
        DateTimeOffset now = clock.GetUtcNow();

        // Keys are matched to anchors with the REVOKE bit cleared, because setting it changes the
        // key tag and digest, and a revoked key must still match the anchor it was trusted as.
        List<(
            DnsResourceRecord Record,
            DnsKeyRecordData Data,
            PersistedTrustAnchor MatchedAnchor
        )> recognized =
        [
            .. result
                .Answer.Where(record =>
                    record.Type == DnsResourceRecordTypeEnum.DnsKey && record.Name == DnsName.Root
                )
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

        List<DnsResourceRecord> dnsKeyRrset = [.. recognized.Select(entry => entry.Record)];

        // RFC 5011 §4: only a key that is Valid, or Missing (still a trust point), may vouch for
        // an update. AddPend, Revoked and Removed keys cannot, and a revoked key is never a
        // signer here (see the self-signed revocation below).
        List<DnsResourceRecord> trustedKeys =
        [
            .. recognized
                .Where(entry =>
                    entry.MatchedAnchor
                        is { State: TrustAnchorStateEnum.Valid or TrustAnchorStateEnum.Missing }
                    && entry.Data.IsZoneKey
                    && !entry.Data.IsRevoked
                )
                .Select(entry => entry.Record),
        ];

        RrsetVerificationResult verification =
            trustedKeys.Count == 0
                ? RrsetVerificationResult.Failure("No trusted key is in the RRset.")
                : RrsetSignatureVerifier.Verify(
                    dnsKeyRrset,
                    result.DnssecRecords,
                    trustedKeys,
                    DnsName.Root,
                    now,
                    new SignatureCheckBudget()
                );

        // RFC 5011 §2.1: a revocation is real only if the revoked key signed the DNSKEY RRset
        // itself, since only the key's holder can do that. Anyone else could replay the RRset with
        // the REVOKE bit set and so switch a trusted key off.
        List<PersistedTrustAnchor> selfRevoked = [];

        foreach (
            (
                DnsResourceRecord revokedRecord,
                _,
                PersistedTrustAnchor matchedAnchor
            ) in recognized.Where(entry =>
                entry.Data.IsRevoked
                && entry.MatchedAnchor
                    is { State: TrustAnchorStateEnum.Valid or TrustAnchorStateEnum.Missing }
            )
        )
        {
            if (
                !selfRevoked.Contains(matchedAnchor)
                && RrsetSignatureVerifier
                    .Verify(
                        dnsKeyRrset,
                        result.DnssecRecords,
                        [revokedRecord],
                        DnsName.Root,
                        now,
                        new SignatureCheckBudget(),
                        acceptRevokedKeys: true
                    )
                    .IsVerified
            )
                selfRevoked.Add(matchedAnchor);
        }

        Dictionary<(ushort, DnssecAlgorithmEnum, byte), PersistedTrustAnchor> updated =
            currentAnchors.ToDictionary(anchor =>
                (anchor.KeyTag, anchor.Algorithm, anchor.DigestType)
            );

        foreach (PersistedTrustAnchor anchor in selfRevoked)
        {
            logger.LogWarning(
                "Trust anchor with key tag {KeyTag} has announced its own revocation; no longer trusting it.",
                anchor.KeyTag
            );
            updated[(anchor.KeyTag, anchor.Algorithm, anchor.DigestType)] = anchor with
            {
                State = TrustAnchorStateEnum.Revoked,
            };

            eventPublisher?.Publish(
                new TrustAnchorRolloverEvent(
                    now,
                    anchor.KeyTag,
                    nameof(TrustAnchorStateEnum.Revoked)
                )
            );
        }

        if (!verification.IsVerified)
        {
            logger.LogWarning(
                "A trust anchor rollover check's root DNSKEY RRset did not verify against any Valid or Missing trust anchor; ignoring it for this check except for self-signed revocations."
            );

            if (selfRevoked.Count > 0)
                store.Save([.. updated.Values]);

            return false;
        }

        lastOriginalTtl = TimeSpan.FromSeconds(
            verification.VerifyingSignature.OriginalTimeToLiveSeconds
        );
        lastExpiration = DateTimeOffset.FromUnixTimeSeconds(
            verification.VerifyingSignature.SignatureExpiration
        );

        foreach (
            (
                DnsResourceRecord record,
                DnsKeyRecordData data,
                PersistedTrustAnchor matchedAnchor
            ) in recognized
        )
        {
            // A key that carries the REVOKE bit without having self-signed is ignored: it neither
            // revokes its anchor nor becomes a candidate.
            if (data.IsRevoked || !data.IsSecureEntryPoint || !data.IsZoneKey)
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
                        now,
                        anchor.KeyTag,
                        nameof(TrustAnchorStateEnum.Valid)
                    )
                );
            }
        }

        // A Valid anchor absent from this RRset (and not revoked above) becomes Missing. One check
        // cannot tell a retirement from a glitch, so the remove hold-down started here decides
        // (see the next loop). A Missing anchor stays a trust point (RFC 5011 §4), so one glitchy
        // check cannot switch validation off. Presence is matched by key tag only.
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
                    now,
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

            // A copy that still carries the REVOKE bit is not a return: it has not self-signed (a
            // self-signed one was handled above) and the key is not usable.
            bool reappeared = recognized.Any(entry =>
                entry.MatchedAnchor?.KeyTag == anchor.KeyTag && !entry.Data.IsRevoked
            );

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
                        now,
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
                    now,
                    anchor.KeyTag,
                    nameof(TrustAnchorStateEnum.Removed)
                )
            );
        }

        store.Save([.. updated.Values]);

        return true;
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
