using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class implements <see cref="IDnssecValidator" />: RFC 4035 §5's chain-of-trust walk,
///     from a configured trust anchor at the root down to whichever zone actually produced
///     <see cref="IterativeResolutionResult" />'s answer (or its negative proof), verifying a
///     DS/DNSKEY pair at every real zone cut along the way.
/// </summary>
/// <param name="engine">
///     The same resolution engine the original query used -- every DS/DNSKEY lookup this
///     validator issues shares its cache and, via the caller-supplied
///     <see cref="QueryBudget" />, its overall bounds too.
/// </param>
/// <param name="cache">
///     Used only to recover which zone owns a name when the original resolution was served
///     entirely from cache, and so carries no <see cref="IterativeResolutionResult.ZoneCutsTraversed" />
///     of its own to walk.
/// </param>
/// <param name="trustAnchorStore">
///     This validator's own current, possibly RFC 5011-updated, set of trusted root keys --
///     never the hardcoded/configured list directly, since that only ever seeds this store the
///     first time it runs.
/// </param>
public sealed class DnssecValidator(
    IIterativeResolutionEngine engine,
    IRecursiveCache cache,
    ITrustAnchorStore trustAnchorStore
) : IDnssecValidator
{
    /// <inheritdoc />
    public async Task<DnssecValidationResult> ValidateAsync(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsClassEnum recordClass,
        IterativeResolutionResult result,
        QueryBudget budget,
        CancellationToken stoppingToken = default
    )
    {
        // RFC 4035 §5.3: a CNAME chain that crosses a zone cut can have each hop owned and signed
        // by a completely different zone than the terminal answer -- so each hop is authenticated
        // independently, against a fresh chain of trust built to *its own* owning zone, before the
        // terminal answer is ever considered. A hop landing in a provably unsigned zone contributes
        // no signature to check (there is nothing to verify against), but still downgrades the
        // overall result to at most Insecure -- a fully Secure verdict requires every real RRset in
        // the whole chain, not merely the final one, to carry a valid, verified signature. A single
        // Bogus/Indeterminate verdict at any hop short-circuits the whole validation immediately;
        // there is no partial credit for a chain that breaks partway down.
        bool anyHopInsecure = false;
        DnsName lastHopZone = null;
        ZoneTrustChainResult lastHopTrust = default;

        foreach (CnameChainHop hop in result.CnameChain)
        {
            if (lastHopZone is null || lastHopZone != hop.OwningZone)
            {
                lastHopTrust = await BuildTrustedKeysForZoneAsync(
                    hop.OwningZone,
                    recordClass,
                    budget,
                    stoppingToken
                );
                lastHopZone = hop.OwningZone;
            }

            if (
                lastHopTrust.State
                is DnssecValidationStateEnum.Bogus
                    or DnssecValidationStateEnum.Indeterminate
            )
                return new DnssecValidationResult(lastHopTrust.State, lastHopTrust.Reason);

            if (lastHopTrust.State == DnssecValidationStateEnum.Insecure)
            {
                // hop.OwningZone is a provably unsigned delegation -- this hop's own CNAME record
                // needs no RRSIG at all, and none is expected; the chain simply can no longer be
                // fully Secure end-to-end.
                anyHopInsecure = true;
                continue;
            }

            if (
                !RrsetSignatureVerifier.TryVerify(
                    [hop.CnameRecord],
                    hop.DnssecRecords,
                    lastHopTrust.TrustedKeys,
                    DateTimeOffset.UtcNow
                )
            )
                return DnssecValidationResult.Bogus(
                    $"The CNAME record for '{hop.CnameRecord.Name}' did not verify against '{hop.OwningZone}''s trusted keys."
                );
        }

        DnsName leafZone =
            result.ZoneCutsTraversed.Count > 0
                ? result.ZoneCutsTraversed[^1]
                : FindOwningZone(queriedName);

        ZoneTrustChainResult terminalTrust = await BuildTrustedKeysForZoneAsync(
            leafZone,
            recordClass,
            budget,
            stoppingToken
        );

        if (
            terminalTrust.State
            is DnssecValidationStateEnum.Bogus
                or DnssecValidationStateEnum.Indeterminate
        )
            return new DnssecValidationResult(terminalTrust.State, terminalTrust.Reason);

        // leafZone itself is a provably unsigned delegation -- whatever it served cannot be
        // authenticated at all, regardless of what the answer/negative-proof content looks like.
        if (terminalTrust.State == DnssecValidationStateEnum.Insecure)
            return DnssecValidationResult.Insecure();

        // The chain of trust now extends all the way to the zone that actually produced the
        // original query's own answer (or negative proof); validate that against it. Only the
        // terminal RRset itself is checked here -- result.Answer also carries every CNAME hop
        // already authenticated above, which must be excluded (by type) or a mixed-type/owner RRset
        // would never verify against any single RRSIG at all.
        if (result.Status == RecursiveResolutionStatusEnum.Success)
        {
            List<DnsResourceRecord> terminalAnswer =
            [
                .. result.Answer.Where(record => record.Type == queriedType),
            ];

            if (
                !RrsetSignatureVerifier.TryVerify(
                    terminalAnswer,
                    result.DnssecRecords,
                    terminalTrust.TrustedKeys,
                    DateTimeOffset.UtcNow
                )
            )
                return DnssecValidationResult.Bogus("The final answer's RRSIG did not verify.");

            return anyHopInsecure
                ? DnssecValidationResult.Insecure()
                : DnssecValidationResult.Secure();
        }

        if (
            result.Status
            is RecursiveResolutionStatusEnum.NxDomain
                or RecursiveResolutionStatusEnum.NoData
        )
        {
            if (!VerifyNegativeResult(queriedName, queriedType, result, terminalTrust.TrustedKeys))
                return DnssecValidationResult.Bogus(
                    "The negative response's denial of existence did not verify."
                );

            return anyHopInsecure
                ? DnssecValidationResult.Insecure()
                : DnssecValidationResult.Secure();
        }

        return DnssecValidationResult.Indeterminate("The underlying resolution did not succeed.");
    }

    /// <summary>
    ///     This record carries <see cref="BuildTrustedKeysForZoneAsync" />'s own outcome: either a
    ///     validated DNSKEY RRset for the target zone (<see cref="DnssecValidationStateEnum.Secure" />),
    ///     confirmation the zone is a provably unsigned delegation
    ///     (<see cref="DnssecValidationStateEnum.Insecure" />, no keys), or a hard failure
    ///     (<see cref="DnssecValidationStateEnum.Bogus" />/<see cref="DnssecValidationStateEnum.Indeterminate" />,
    ///     with a reason).
    /// </summary>
    private readonly record struct ZoneTrustChainResult(
        DnssecValidationStateEnum State,
        IReadOnlyList<DnsResourceRecord> TrustedKeys,
        string Reason
    )
    {
        public static ZoneTrustChainResult Secure(IReadOnlyList<DnsResourceRecord> keys) =>
            new(DnssecValidationStateEnum.Secure, keys, null);

        public static ZoneTrustChainResult Insecure() =>
            new(DnssecValidationStateEnum.Insecure, null, null);

        public static ZoneTrustChainResult Bogus(string reason) =>
            new(DnssecValidationStateEnum.Bogus, null, reason);

        public static ZoneTrustChainResult Indeterminate(string reason) =>
            new(DnssecValidationStateEnum.Indeterminate, null, reason);
    }

    /// <summary>
    ///     This method walks RFC 4035 §5's chain of trust from the root down to
    ///     <paramref name="targetZone" />, returning either that zone's own validated DNSKEY RRset,
    ///     confirmation the zone is a provably unsigned delegation, or the specific failure that
    ///     broke the chain -- the shared walk both the terminal answer's own zone and every
    ///     independent CNAME-chain hop's owning zone need, generalized from what was originally a
    ///     single hardcoded walk to just the terminal leaf zone.
    /// </summary>
    private async Task<ZoneTrustChainResult> BuildTrustedKeysForZoneAsync(
        DnsName targetZone,
        DnsClassEnum recordClass,
        QueryBudget budget,
        CancellationToken stoppingToken
    )
    {
        List<DnsName> realChain;
        IterativeResolutionResult targetDsResult = null;

        if (targetZone == DnsName.Root)
        {
            realChain = [DnsName.Root];
        }
        else
        {
            // A single DS query for the target zone, forced to start from the root, passively
            // rediscovers every *real* intermediate zone cut on the way -- which the original
            // query's own ZoneCutsTraversed might have skipped straight past, if a closer
            // ancestor happened to already be cached from a completely unrelated prior query.
            targetDsResult = await engine.ResolveAsync(
                targetZone,
                DnsResourceRecordTypeEnum.DS,
                recordClass,
                true,
                budget,
                forceRootStart: true,
                clientSubnetOption: null,
                stoppingToken: stoppingToken
            );
            realChain = [.. targetDsResult.ZoneCutsTraversed, targetZone];
        }

        // Walk realChain from the root outward, one real zone cut at a time, building up
        // trustedKeys as we go: at each step, trustedKeys holds the *previous* zone's own
        // already-verified DNSKEY RRset, which is exactly what authenticates the *next* zone's
        // own DS (and, transitively, its DNSKEY) -- the textbook RFC 4035 §5 chain-of-trust walk.
        IReadOnlyList<DnsResourceRecord> trustedKeys = null;

        for (int i = 0; i < realChain.Count; i++)
        {
            DnsName zone = realChain[i];

            if (i == 0)
            {
                // The root of the chain has no parent DS to check against -- it is instead
                // verified directly against this resolver's own configured/RFC-5011-updated
                // trust anchors: fetch the root's DNSKEY RRset, find the specific key whose own
                // digest matches a trusted anchor, and verify the whole RRset's signature under
                // that one key.
                IterativeResolutionResult dnsKeyResult = await ResolveDnsKeyAsync(
                    zone,
                    recordClass,
                    budget,
                    stoppingToken
                );

                if (dnsKeyResult is null)
                    return ZoneTrustChainResult.Indeterminate(
                        "No DNSKEY RRset could be retrieved for the root zone."
                    );

                IReadOnlyList<TrustAnchor> anchors = trustAnchorStore.GetValidAnchors();
                DnsResourceRecord matchingKey = RrsetSignatureVerifier.FindKeyMatchingTrustAnchor(
                    dnsKeyResult.Answer,
                    zone,
                    anchors
                );

                if (matchingKey is null)
                    return ZoneTrustChainResult.Indeterminate(
                        "The root zone's DNSKEY RRset matches none of this resolver's configured trust anchors."
                    );

                if (
                    !RrsetSignatureVerifier.TryVerify(
                        dnsKeyResult.Answer,
                        dnsKeyResult.DnssecRecords,
                        [matchingKey],
                        DateTimeOffset.UtcNow
                    )
                )
                    return ZoneTrustChainResult.Bogus(
                        "The root zone's DNSKEY RRset did not verify against the trust anchor."
                    );

                trustedKeys = dnsKeyResult.Answer;
                continue;
            }

            // Every zone after the root is authenticated in two steps: first its own DS record
            // (fetched from, and verified against, the parent zone's already-trusted DNSKEY set
            // held in trustedKeys), then its own DNSKEY RRset -- matched against that DS by
            // digest and verified by signature -- which becomes the new trustedKeys for the
            // next zone one level deeper. A missing DS is only acceptable when its own absence
            // can itself be proven authentic (an unsigned delegation); anything else along this
            // path is Bogus.
            DnsName parentZone = realChain[i - 1];
            IterativeResolutionResult dsResult =
                zone == targetZone && targetDsResult is not null
                    ? targetDsResult
                    : await engine.ResolveAsync(
                        zone,
                        DnsResourceRecordTypeEnum.DS,
                        recordClass,
                        true,
                        budget,
                        forceRootStart: false,
                        clientSubnetOption: null,
                        stoppingToken: stoppingToken
                    );

            if (
                dsResult.Status
                is RecursiveResolutionStatusEnum.NxDomain
                    or RecursiveResolutionStatusEnum.NoData
            )
                return VerifyAbsenceOfDs(zone, dsResult, trustedKeys)
                    ? ZoneTrustChainResult.Insecure()
                    : ZoneTrustChainResult.Bogus(
                        $"No DS record exists for '{zone}', and its absence could not be authenticated."
                    );

            if (dsResult.Status != RecursiveResolutionStatusEnum.Success)
                return ZoneTrustChainResult.Indeterminate(
                    $"Could not resolve a DS record for '{zone}'."
                );

            if (
                !RrsetSignatureVerifier.TryVerify(
                    dsResult.Answer,
                    dsResult.DnssecRecords,
                    trustedKeys,
                    DateTimeOffset.UtcNow
                )
            )
                return ZoneTrustChainResult.Bogus(
                    $"The DS RRset for '{zone}' did not verify against '{parentZone}''s trusted keys."
                );

            IterativeResolutionResult dnsKeyForZone = await ResolveDnsKeyAsync(
                zone,
                recordClass,
                budget,
                stoppingToken
            );

            if (dnsKeyForZone is null)
                return ZoneTrustChainResult.Bogus(
                    $"'{zone}' has a DS record, but no DNSKEY RRset could be retrieved for it."
                );

            DnsResourceRecord matchingKeyForZone = RrsetSignatureVerifier.FindKeyMatchingDs(
                dnsKeyForZone.Answer,
                zone,
                dsResult.Answer
            );

            if (matchingKeyForZone is null)
                return ZoneTrustChainResult.Bogus(
                    $"No DNSKEY at '{zone}' matches its own DS record."
                );

            if (
                !RrsetSignatureVerifier.TryVerify(
                    dnsKeyForZone.Answer,
                    dnsKeyForZone.DnssecRecords,
                    [matchingKeyForZone],
                    DateTimeOffset.UtcNow
                )
            )
                return ZoneTrustChainResult.Bogus($"The DNSKEY RRset for '{zone}' did not verify.");

            trustedKeys = dnsKeyForZone.Answer;
        }

        return ZoneTrustChainResult.Secure(trustedKeys);
    }

    /// <summary>
    ///     This method resolves a zone's own DNSKEY RRset, returning <see langword="null" /> if
    ///     that resolution did not succeed at all.
    /// </summary>
    private async Task<IterativeResolutionResult> ResolveDnsKeyAsync(
        DnsName zone,
        DnsClassEnum recordClass,
        QueryBudget budget,
        CancellationToken stoppingToken
    )
    {
        IterativeResolutionResult result = await engine.ResolveAsync(
            zone,
            DnsResourceRecordTypeEnum.DnsKey,
            recordClass,
            true,
            budget,
            forceRootStart: false,
            clientSubnetOption: null,
            stoppingToken: stoppingToken
        );

        return result.Status == RecursiveResolutionStatusEnum.Success ? result : null;
    }

    /// <summary>
    ///     This method finds the closest zone cut this resolver already has cached name servers
    ///     for -- the same walk <see cref="IterativeResolutionEngine" /> itself does internally,
    ///     needed here only because a cache-hit <see cref="IterativeResolutionResult" /> carries
    ///     no zone-cut provenance of its own.
    /// </summary>
    private DnsName FindOwningZone(DnsName name)
    {
        for (int start = 0; start <= name.Labels.Count; start++)
        {
            DnsName candidate = new([.. name.Labels.Skip(start)]);
            RecursiveCacheEntry cached = cache.TryGet(
                candidate,
                DnsResourceRecordTypeEnum.NS,
                DnsClassEnum.Internet
            );

            if (cached is { IsNegative: false } && cached.Records.Count > 0)
                return candidate;
        }

        return DnsName.Root;
    }

    /// <summary>
    ///     This method proves that a DS record's absence at a delegation point is authentic
    ///     rather than merely missing, per RFC 4035 §5.4 / RFC 5155 §5.2/§8.1.
    /// </summary>
    private static bool VerifyAbsenceOfDs(
        DnsName zone,
        IterativeResolutionResult dsResult,
        IReadOnlyList<DnsResourceRecord> parentTrustedKeys
    )
    {
        if (!VerifyAllNsecOrNsec3Signatures(dsResult.DnssecRecords, parentTrustedKeys))
            return false;

        bool usesNsec3 = dsResult.DnssecRecords.Any(record =>
            record.Type == DnsResourceRecordTypeEnum.Nsec3
        );

        if (!usesNsec3)
            return DenialOfExistenceValidator.VerifyNoData(
                zone,
                DnsResourceRecordTypeEnum.DS,
                dsResult.DnssecRecords
            );

        return DenialOfExistenceValidator.VerifyNoDataNsec3(
                zone,
                DnsResourceRecordTypeEnum.DS,
                dsResult.DnssecRecords
            ) || DenialOfExistenceValidator.VerifyOptOut(zone, dsResult.DnssecRecords);
    }

    /// <summary>
    ///     This method proves the original query's own negative response (NXDOMAIN/NODATA) is
    ///     authentic, per RFC 4035 §5.4 / RFC 5155 §8.
    /// </summary>
    private static bool VerifyNegativeResult(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        IterativeResolutionResult result,
        IReadOnlyList<DnsResourceRecord> trustedKeys
    )
    {
        if (!VerifyAllNsecOrNsec3Signatures(result.DnssecRecords, trustedKeys))
            return false;

        bool usesNsec3 = result.DnssecRecords.Any(record =>
            record.Type == DnsResourceRecordTypeEnum.Nsec3
        );

        if (result.Status == RecursiveResolutionStatusEnum.NoData)
            return usesNsec3
                ? DenialOfExistenceValidator.VerifyNoDataNsec3(
                    queriedName,
                    queriedType,
                    result.DnssecRecords
                )
                : DenialOfExistenceValidator.VerifyNoData(
                    queriedName,
                    queriedType,
                    result.DnssecRecords
                );

        return usesNsec3
            ? DenialOfExistenceValidator.VerifyNxDomainNsec3(queriedName, result.DnssecRecords)
            : DenialOfExistenceValidator.VerifyNxDomain(queriedName, result.DnssecRecords);
    }

    /// <summary>
    ///     This method verifies every distinct NSEC/NSEC3 RRset present -- each one is its own
    ///     RRset, with its own owner name and its own RRSIG, since two NSEC3 records covering
    ///     different hash ranges are never the same RRset -- against a set of already-trusted
    ///     keys, so a fabricated (but structurally covering) record without a valid signature
    ///     cannot "prove" anything.
    /// </summary>
    private static bool VerifyAllNsecOrNsec3Signatures(
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        IReadOnlyList<DnsResourceRecord> trustedKeys
    )
    {
        bool foundAny = false;

        foreach (
            var group in dnssecRecords
                .Where(record =>
                    record.Type is DnsResourceRecordTypeEnum.Nsec or DnsResourceRecordTypeEnum.Nsec3
                )
                .GroupBy(record => (record.Name, record.Type))
        )
        {
            foundAny = true;

            if (
                !RrsetSignatureVerifier.TryVerify(
                    [.. group],
                    dnssecRecords,
                    trustedKeys,
                    DateTimeOffset.UtcNow
                )
            )
                return false;
        }

        return foundAny;
    }
}
