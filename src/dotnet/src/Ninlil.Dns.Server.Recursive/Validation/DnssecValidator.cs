using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class implements <see cref="IDnssecValidator" /> with the RFC 4035 §5 chain-of-trust
///     walk: from a trust anchor at the root down to the zone that produced a result's answer or
///     negative proof, verifying the DS and DNSKEY at every zone cut. Each CNAME hop is checked
///     against its own zone first. It fetches DS and DNSKEY records through the engine, sharing
///     the caller's <see cref="QueryBudget" />. The answer's zone is the last of the result's
///     <see cref="IterativeResolutionResult.ZoneCutsTraversed" />, and the chain to it is
///     rediscovered with a DS query for that zone started at the root.
/// </summary>
/// <param name="engine">
///     The resolution engine for the DS and DNSKEY lookups; it shares the resolver's cache, and
///     the lookups count against the caller's <see cref="QueryBudget" />.
/// </param>
/// <param name="cache">
///     Used only to find a name's zone when a result has no
///     <see cref="IterativeResolutionResult.ZoneCutsTraversed" />.
/// </param>
/// <param name="trustAnchorStore">
///     The current trusted root keys, kept up to date by RFC 5011 rollover. The built-in or
///     configured list only seeds this store.
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
        // RFC 4035 §5.3: each CNAME hop may be signed by a different zone, so each is checked
        // against a chain of trust to its own zone before the final answer. A hop in a provably
        // unsigned zone has nothing to check but caps the result at Insecure, since Secure needs
        // every RRset in the chain verified. A Bogus or Indeterminate hop ends validation.
        bool anyHopInsecure = false;
        SignatureCheckBudget signatureBudget = new();
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
                    signatureBudget,
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
                // The hop's zone is provably unsigned, so no RRSIG is expected, but the chain can
                // no longer be Secure.
                anyHopInsecure = true;
                continue;
            }

            RrsetVerificationResult hopVerification = RrsetSignatureVerifier.Verify(
                [hop.CnameRecord],
                hop.DnssecRecords,
                lastHopTrust.TrustedKeys,
                hop.OwningZone,
                DateTimeOffset.UtcNow,
                signatureBudget
            );

            if (!hopVerification.IsVerified)
                return DnssecValidationResult.Bogus(
                    $"The CNAME record for '{hop.CnameRecord.Name}' did not verify against '{hop.OwningZone}''s trusted keys: {hopVerification.FailureReason}"
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
            signatureBudget,
            stoppingToken
        );

        if (
            terminalTrust.State
            is DnssecValidationStateEnum.Bogus
                or DnssecValidationStateEnum.Indeterminate
        )
            return new DnssecValidationResult(terminalTrust.State, terminalTrust.Reason);

        // The answer's zone is provably unsigned, so nothing it served can be authenticated.
        if (terminalTrust.State == DnssecValidationStateEnum.Insecure)
            return DnssecValidationResult.Insecure();

        // The chain now reaches the answer's zone. Check only the final RRset: result.Answer also
        // holds the CNAME hops checked above, which are filtered out by type, since a mixed RRset
        // would match no RRSIG. A negative answer's NSEC/NSEC3 records are checked; its SOA
        // signature is not.
        if (result.Status == RecursiveResolutionStatusEnum.Success)
        {
            List<DnsResourceRecord> terminalAnswer =
            [
                .. result.Answer.Where(record => record.Type == queriedType),
            ];

            RrsetVerificationResult answerVerification = RrsetSignatureVerifier.Verify(
                terminalAnswer,
                result.DnssecRecords,
                terminalTrust.TrustedKeys,
                leafZone,
                DateTimeOffset.UtcNow,
                signatureBudget
            );

            if (!answerVerification.IsVerified)
                return DnssecValidationResult.Bogus(
                    $"The final answer's RRSIG did not verify: {answerVerification.FailureReason}"
                );

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
            DenialOfExistenceOutcomeEnum denial = VerifyNegativeResult(
                queriedName,
                queriedType,
                leafZone,
                result,
                terminalTrust.TrustedKeys,
                signatureBudget
            );

            if (denial == DenialOfExistenceOutcomeEnum.NotProven)
                return DnssecValidationResult.Bogus(
                    "The negative response's denial of existence did not verify."
                );

            // An Opt-Out span or an NSEC3 iteration count above the cap proves the name is not
            // secured by the zone: the answer is served without the AD bit.
            return anyHopInsecure || denial == DenialOfExistenceOutcomeEnum.ProvenInsecure
                ? DnssecValidationResult.Insecure()
                : DnssecValidationResult.Secure();
        }

        return DnssecValidationResult.Indeterminate("The underlying resolution did not succeed.");
    }

    /// <summary>
    ///     This record carries the outcome of <see cref="BuildTrustedKeysForZoneAsync" />: the zone's
    ///     validated DNSKEY RRset (<see cref="DnssecValidationStateEnum.Secure" />), proof the zone
    ///     is unsigned (<see cref="DnssecValidationStateEnum.Insecure" />, no keys), or a failure
    ///     (<see cref="DnssecValidationStateEnum.Bogus" /> or <see cref="DnssecValidationStateEnum.Indeterminate" />,
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
    ///     This method walks the RFC 4035 §5 chain of trust down to <paramref name="targetZone" />
    ///     and returns the zone's validated DNSKEY RRset, proof it is unsigned, or the failure that
    ///     broke the chain. It serves both the answer's zone and each CNAME hop's zone.
    ///     <para>
    ///         The chain is the zone cuts of a DS query for the target started at the root. If
    ///         that DS answer is already cached, the engine returns it without walking, and the
    ///         chain then starts at the closest cached cut rather than the root; the first element
    ///         is always checked against the root trust anchors.
    ///     </para>
    /// </summary>
    private async Task<ZoneTrustChainResult> BuildTrustedKeysForZoneAsync(
        DnsName targetZone,
        DnsClassEnum recordClass,
        QueryBudget budget,
        SignatureCheckBudget signatureBudget,
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
            // A DS query for the target, started at the root, finds every zone cut on the way,
            // which the original query may have skipped by starting from a cached ancestor.
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

        // Walk realChain from the top. At each step trustedKeys holds the previous zone's
        // verified DNSKEY RRset, which authenticates the next zone's DS and so its DNSKEY.
        IReadOnlyList<DnsResourceRecord> trustedKeys = null;

        for (int i = 0; i < realChain.Count; i++)
        {
            DnsName zone = realChain[i];

            if (i == 0)
            {
                // The top of the chain has no parent DS. Its DNSKEY RRset must contain a key
                // matching a valid trust anchor, and be signed by that key.
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

                // Only DNSKEY records owned by the zone count; a CNAME or foreign record in the
                // answer is not part of the zone's key set.
                List<DnsResourceRecord> rootKeys = OwnedRecords(
                    dnsKeyResult.Answer,
                    DnsResourceRecordTypeEnum.DnsKey,
                    zone
                );
                IReadOnlyList<TrustAnchor> anchors = trustAnchorStore.GetValidAnchors();
                DnsResourceRecord matchingKey = RrsetSignatureVerifier.FindKeyMatchingTrustAnchor(
                    rootKeys,
                    zone,
                    anchors
                );

                if (matchingKey is null)
                    return ZoneTrustChainResult.Indeterminate(
                        "The root zone's DNSKEY RRset matches none of this resolver's configured trust anchors."
                    );

                RrsetVerificationResult rootVerification = RrsetSignatureVerifier.Verify(
                    rootKeys,
                    dnsKeyResult.DnssecRecords,
                    [matchingKey],
                    zone,
                    DateTimeOffset.UtcNow,
                    signatureBudget
                );

                if (!rootVerification.IsVerified)
                    return ZoneTrustChainResult.Bogus(
                        $"The root zone's DNSKEY RRset did not verify against the trust anchor: {rootVerification.FailureReason}"
                    );

                trustedKeys = rootKeys;
                continue;
            }

            // Each later zone takes two steps: verify its DS with the parent's keys, then find the
            // DNSKEY matching that DS and verify the DNSKEY RRset with it; that RRset becomes
            // trustedKeys. A missing DS is accepted only with a signed proof of absence (an
            // unsigned delegation, Insecure); otherwise the chain is Bogus.
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
                return
                    VerifyAbsenceOfDs(zone, parentZone, dsResult, trustedKeys, signatureBudget)
                    != DenialOfExistenceOutcomeEnum.NotProven
                    ? ZoneTrustChainResult.Insecure()
                    : ZoneTrustChainResult.Bogus(
                        $"No DS record exists for '{zone}', and its absence could not be authenticated."
                    );

            if (dsResult.Status != RecursiveResolutionStatusEnum.Success)
                return ZoneTrustChainResult.Indeterminate(
                    $"Could not resolve a DS record for '{zone}'."
                );

            // A CNAME or foreign record in the answer is not part of the DS RRset.
            List<DnsResourceRecord> dsRrset = OwnedRecords(
                dsResult.Answer,
                DnsResourceRecordTypeEnum.DS,
                zone
            );
            RrsetVerificationResult dsVerification = RrsetSignatureVerifier.Verify(
                dsRrset,
                dsResult.DnssecRecords,
                trustedKeys,
                parentZone,
                DateTimeOffset.UtcNow,
                signatureBudget
            );

            if (!dsVerification.IsVerified)
                return ZoneTrustChainResult.Bogus(
                    $"The DS RRset for '{zone}' did not verify against '{parentZone}''s trusted keys: {dsVerification.FailureReason}"
                );

            // RFC 4035 §5.2: a DS RRset with no digest type or algorithm this validator supports
            // makes the zone Insecure, not Bogus.
            if (!RrsetSignatureVerifier.HasSupportedDs(dsRrset))
                return ZoneTrustChainResult.Insecure();

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

            List<DnsResourceRecord> zoneKeys = OwnedRecords(
                dnsKeyForZone.Answer,
                DnsResourceRecordTypeEnum.DnsKey,
                zone
            );
            IReadOnlyList<DnsResourceRecord> matchingKeysForZone =
                RrsetSignatureVerifier.FindKeysMatchingDs(zoneKeys, zone, dsRrset);

            if (matchingKeysForZone.Count == 0)
                return ZoneTrustChainResult.Bogus(
                    $"No DNSKEY at '{zone}' matches its own DS record."
                );

            RrsetVerificationResult zoneKeysVerification = RrsetSignatureVerifier.Verify(
                zoneKeys,
                dnsKeyForZone.DnssecRecords,
                matchingKeysForZone,
                zone,
                DateTimeOffset.UtcNow,
                signatureBudget
            );

            if (!zoneKeysVerification.IsVerified)
                return ZoneTrustChainResult.Bogus(
                    $"The DNSKEY RRset for '{zone}' did not verify: {zoneKeysVerification.FailureReason}"
                );

            trustedKeys = zoneKeys;
        }

        return ZoneTrustChainResult.Secure(trustedKeys);
    }

    /// <summary>
    ///     This method keeps the records of one type and owner name from an answer, so a CNAME or
    ///     an unrelated record that came with a DS or DNSKEY answer never reaches signature
    ///     verification as part of the RRset.
    /// </summary>
    private static List<DnsResourceRecord> OwnedRecords(
        IReadOnlyList<DnsResourceRecord> records,
        DnsResourceRecordTypeEnum type,
        DnsName owner
    ) => [.. records.Where(record => record.Type == type && record.Name == owner)];

    /// <summary>
    ///     This method resolves a zone's DNSKEY RRset, or returns <see langword="null" /> if the
    ///     resolution did not succeed.
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
    ///     This method finds the closest zone cut with cached name servers, the same walk
    ///     <see cref="IterativeResolutionEngine" /> does, for a result with no zone cuts.
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
    ///     This method checks the signed proof that a delegation has no DS (RFC 4035 §5.4,
    ///     RFC 6840 §4.4, RFC 5155 §8.6): an NSEC or matching NSEC3 for the delegation name with NS
    ///     set, SOA clear and DS clear, or an Opt-Out NSEC3 covering it. The records belong to the
    ///     parent zone, so the parent's apex bounds them.
    /// </summary>
    private static DenialOfExistenceOutcomeEnum VerifyAbsenceOfDs(
        DnsName zone,
        DnsName parentZone,
        IterativeResolutionResult dsResult,
        IReadOnlyList<DnsResourceRecord> parentTrustedKeys,
        SignatureCheckBudget signatureBudget
    )
    {
        if (
            !VerifyAllNsecOrNsec3Signatures(
                dsResult.DnssecRecords,
                parentTrustedKeys,
                parentZone,
                signatureBudget
            )
        )
            return DenialOfExistenceOutcomeEnum.NotProven;

        bool usesNsec3 = dsResult.DnssecRecords.Any(record =>
            record.Type == DnsResourceRecordTypeEnum.Nsec3
        );

        return usesNsec3
            ? DenialOfExistenceValidator.VerifyNoDataNsec3(
                zone,
                DnsResourceRecordTypeEnum.DS,
                parentZone,
                dsResult.DnssecRecords
            )
            : DenialOfExistenceValidator.VerifyNoData(
                zone,
                DnsResourceRecordTypeEnum.DS,
                parentZone,
                dsResult.DnssecRecords
            );
    }

    /// <summary>
    ///     This method checks the signed proof for a negative answer (NXDOMAIN or NODATA), per
    ///     RFC 4035 §5.4 and RFC 5155 §8. The records belong to the zone that answered, whose apex
    ///     bounds them.
    /// </summary>
    private static DenialOfExistenceOutcomeEnum VerifyNegativeResult(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsName zoneApex,
        IterativeResolutionResult result,
        IReadOnlyList<DnsResourceRecord> trustedKeys,
        SignatureCheckBudget signatureBudget
    )
    {
        if (
            !VerifyAllNsecOrNsec3Signatures(
                result.DnssecRecords,
                trustedKeys,
                zoneApex,
                signatureBudget
            )
        )
            return DenialOfExistenceOutcomeEnum.NotProven;

        bool usesNsec3 = result.DnssecRecords.Any(record =>
            record.Type == DnsResourceRecordTypeEnum.Nsec3
        );

        if (result.Status == RecursiveResolutionStatusEnum.NoData)
            return usesNsec3
                ? DenialOfExistenceValidator.VerifyNoDataNsec3(
                    queriedName,
                    queriedType,
                    zoneApex,
                    result.DnssecRecords
                )
                : DenialOfExistenceValidator.VerifyNoData(
                    queriedName,
                    queriedType,
                    zoneApex,
                    result.DnssecRecords
                );

        return usesNsec3
            ? DenialOfExistenceValidator.VerifyNxDomainNsec3(
                queriedName,
                zoneApex,
                result.DnssecRecords
            )
            : DenialOfExistenceValidator.VerifyNxDomain(
                queriedName,
                zoneApex,
                result.DnssecRecords
            );
    }

    /// <summary>
    ///     This method verifies the signature of every NSEC and NSEC3 RRset present (each owner
    ///     name is its own RRset) against the keys of <c>zoneApex</c>, so an unsigned record cannot
    ///     prove anything. It fails when there are none.
    /// </summary>
    private static bool VerifyAllNsecOrNsec3Signatures(
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        IReadOnlyList<DnsResourceRecord> trustedKeys,
        DnsName zoneApex,
        SignatureCheckBudget signatureBudget
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
                !RrsetSignatureVerifier
                    .Verify(
                        [.. group],
                        dnssecRecords,
                        trustedKeys,
                        zoneApex,
                        DateTimeOffset.UtcNow,
                        signatureBudget
                    )
                    .IsVerified
            )
                return false;
        }

        return foundAny;
    }
}
