using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class implements <see cref="IDnssecValidator" /> with the RFC 4035 §5 chain of trust.
///     It is built around the signer name of each RRSIG and a per-zone trust result
///     (<see cref="ZoneTrust" />), not around where the resolution happened to go.
///     <para>
///         <b>Zone trust.</b> The root is secure when its DNSKEY RRset is signed by a key that
///         matches a trust anchor. Any other zone takes its parent's state: when the parent is
///         secure, a signed DS RRset and a DNSKEY RRset that a DS-matching key signs make the zone
///         secure, a signed proof of no DS (RFC 4035 §5.2, RFC 6840 §4.4) makes it insecure, and
///         anything else is bogus. A zone's parent is the signer of its DS or NSEC proof, so a
///         DS answer from the cache, or a parent and child served by the same name servers, build
///         the same chain as a walk from the root. Results are kept in
///         <see cref="DnssecTrustCache" /> and in a per-validation memo.
///     </para>
///     <para>
///         <b>RRsets.</b> An RRset is verified against the zone named by its RRSIG's signer.
///         Without an RRSIG the answer is signed or unsigned depending on whether a delegation
///         with a DS sits between the closest known zone and the owner (the unsigned walk), and an
///         unsigned answer in a signed zone is bogus. A wildcard expansion needs the NSEC or NSEC3
///         proof that no closer name exists (RFC 4035 §5.3.4, RFC 5155 §8.8). A negative answer
///         needs its NSEC or NSEC3 proof for the last name of the CNAME chain.
///     </para>
///     <para>
///         <b>Failures.</b> With a trust anchor present, any failure to obtain or verify data the
///         chain needs (a failed DS or DNSKEY lookup, an exhausted budget, a key that matches no
///         anchor) is <see cref="DnssecValidationStateEnum.Bogus" />. Only the absence of a usable
///         trust anchor gives <see cref="DnssecValidationStateEnum.Indeterminate" />. Signature
///         work is bounded by one <see cref="SignatureCheckBudget" /> per validation.
///     </para>
///     <para>
///         The DS and DNSKEY data come from an <see cref="IDnssecRecordFetcher" />: the iterative
///         engine by default, or the one a caller passes to
///         <see cref="ValidateAsync" />.
///     </para>
/// </summary>
/// <param name="engine">
///     The resolution engine behind the default <see cref="IDnssecRecordFetcher" />; it shares the
///     resolver's cache, and the lookups count against the caller's <see cref="QueryBudget" />.
/// </param>
/// <param name="trustAnchorStore">
///     The current trusted root keys, kept up to date by RFC 5011 rollover. The built-in or
///     configured list only seeds this store.
/// </param>
/// <param name="trustCache">The cache of proven zone results, shared by all validations.</param>
/// <param name="timeProvider">The clock for signature validity windows; the system clock by default.</param>
public sealed class DnssecValidator(
    IIterativeResolutionEngine engine,
    ITrustAnchorStore trustAnchorStore,
    DnssecTrustCache trustCache,
    TimeProvider timeProvider = null
) : IDnssecValidator
{
    /// <summary>
    ///     This constant bounds how many signer names of one RRset are tried. An RRset has one
    ///     signing zone; the second slot covers a junk RRSIG that names a different ancestor.
    /// </summary>
    private const int MaxSignersTried = 2;

    private readonly IDnssecRecordFetcher _defaultFetcher = new IterativeDnssecRecordFetcher(
        engine
    );
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<DnssecValidationResult> ValidateAsync(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsClassEnum recordClass,
        IterativeResolutionResult result,
        QueryBudget budget,
        CancellationToken stoppingToken = default,
        IDnssecRecordFetcher fetcher = null
    )
    {
        IReadOnlyList<TrustAnchor> anchors = trustAnchorStore.GetValidAnchors();

        // Indeterminate means exactly this: there is nothing to start a chain from.
        if (anchors.Count == 0)
            return DnssecValidationResult.Indeterminate("No usable trust anchor is configured.");

        ValidationContext context = new(
            fetcher ?? _defaultFetcher,
            recordClass,
            budget,
            anchors,
            AnchorFingerprint(anchors),
            _timeProvider.GetUtcNow(),
            stoppingToken
        );

        DnssecValidationResult outcome = await ValidateResultAsync(
            context,
            queriedName,
            queriedType,
            result
        );

        return outcome with
        {
            SignatureChecksUsed = context.Signatures.TotalUsed,
        };
    }

    /// <summary>
    ///     This record holds what one validation shares across its steps.
    /// </summary>
    private sealed class ValidationContext(
        IDnssecRecordFetcher fetcher,
        DnsClassEnum recordClass,
        QueryBudget budget,
        IReadOnlyList<TrustAnchor> anchors,
        string anchorFingerprint,
        DateTimeOffset now,
        CancellationToken stoppingToken
    )
    {
        public IDnssecRecordFetcher Fetcher { get; } = fetcher;
        public DnsClassEnum Class { get; } = recordClass;
        public QueryBudget Budget { get; } = budget;
        public IReadOnlyList<TrustAnchor> Anchors { get; } = anchors;
        public string AnchorFingerprint { get; } = anchorFingerprint;
        public DateTimeOffset Now { get; } = now;
        public CancellationToken StoppingToken { get; } = stoppingToken;
        public SignatureCheckBudget Signatures { get; } = new();

        /// <summary>
        ///     This property memoizes the zone results of this validation, so a zone shared by
        ///     several RRsets (a CNAME hop and the answer) is computed once even when it is not
        ///     cacheable.
        /// </summary>
        public Dictionary<DnsName, ZoneTrust> Memo { get; } = [];
    }

    /// <summary>
    ///     This method validates the CNAME hops, then the final RRset or the negative proof, and
    ///     caps the result at Insecure when any hop was insecure.
    /// </summary>
    private async Task<DnssecValidationResult> ValidateResultAsync(
        ValidationContext context,
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        IterativeResolutionResult result
    )
    {
        if (
            result.Status
            is not (
                RecursiveResolutionStatusEnum.Success
                or RecursiveResolutionStatusEnum.NxDomain
                or RecursiveResolutionStatusEnum.NoData
            )
        )
            return DnssecValidationResult.Indeterminate(
                "The underlying resolution did not succeed."
            );

        // RFC 4035 §5.3: each CNAME hop is an RRset of its own, signed by the zone that holds it.
        // A hop in a provably unsigned zone caps the result at Insecure, since Secure needs every
        // RRset in the chain verified.
        bool anyHopInsecure = false;
        DnsName finalName = queriedName;

        foreach (CnameChainHop hop in result.CnameChain)
        {
            DnssecValidationResult hopOutcome = await ValidateRrsetAsync(
                context,
                [hop.CnameRecord],
                hop.DnssecRecords,
                hop.OwningZone,
                $"The CNAME record for '{hop.CnameRecord.Name}'"
            );

            if (
                hopOutcome.State
                is not (DnssecValidationStateEnum.Secure or DnssecValidationStateEnum.Insecure)
            )
                return hopOutcome;

            anyHopInsecure |= hopOutcome.State == DnssecValidationStateEnum.Insecure;

            if (
                DnsRecordDataCodecRegistry.Decode(
                    DnsResourceRecordTypeEnum.Cname,
                    hop.CnameRecord.Data
                )
                is not CnameRecordData cnameData
            )
                return DnssecValidationResult.Bogus(
                    $"The CNAME record for '{hop.CnameRecord.Name}' is malformed."
                );

            finalName = cnameData.Target;
        }

        DnssecValidationResult outcome =
            result.Status == RecursiveResolutionStatusEnum.Success
                ? await ValidateAnswerAsync(context, finalName, queriedType, result)
                : await ValidateNegativeAsync(context, finalName, queriedType, result);

        return anyHopInsecure && outcome.State == DnssecValidationStateEnum.Secure
            ? DnssecValidationResult.Insecure()
            : outcome;
    }

    /// <summary>
    ///     This method validates the final RRset of a positive answer: the records of the queried
    ///     type owned by the last name of the CNAME chain.
    /// </summary>
    private Task<DnssecValidationResult> ValidateAnswerAsync(
        ValidationContext context,
        DnsName finalName,
        DnsResourceRecordTypeEnum queriedType,
        IterativeResolutionResult result
    )
    {
        List<DnsResourceRecord> rrset =
        [
            .. result.Answer.Where(record =>
                record.Type == queriedType && record.Name == finalName
            ),
        ];

        if (rrset.Count == 0)
            return Task.FromResult(
                DnssecValidationResult.Bogus(
                    $"The answer holds no {queriedType} record for '{finalName}'."
                )
            );

        return ValidateRrsetAsync(
            context,
            rrset,
            result.DnssecRecords,
            result.ZoneCutsTraversed.LastOrDefault(),
            $"The final {queriedType} RRset for '{finalName}'"
        );
    }

    /// <summary>
    ///     This method validates the proof of a negative answer for <paramref name="finalName" />,
    ///     the last name of the CNAME chain (the queried name when there is none): the zone is the
    ///     owner of the SOA, or the signer of the NSEC or NSEC3 records, and the proof must hold
    ///     for that name (RFC 4035 §5.4, RFC 5155 §8).
    /// </summary>
    private async Task<DnssecValidationResult> ValidateNegativeAsync(
        ValidationContext context,
        DnsName finalName,
        DnsResourceRecordTypeEnum queriedType,
        IterativeResolutionResult result
    )
    {
        DnsName zone = null;

        if (result.Soa is not null && IsAtOrBelow(finalName, result.Soa.Name))
            zone = result.Soa.Name;
        else
            zone = SignaturesOf(
                    result.DnssecRecords,
                    DnsResourceRecordTypeEnum.Nsec,
                    DnsResourceRecordTypeEnum.Nsec3
                )
                .Select(signature => signature.Data.SignerName)
                .Where(signer => IsAtOrBelow(finalName, signer))
                .OrderByDescending(signer => signer.Labels.Count)
                .FirstOrDefault();

        // No SOA and no signed proof: the response claims nothing from a signed zone, so it is
        // insecure only if the walk down from the closest known zone finds an unsigned delegation.
        if (zone is null)
            return await UnsignedWalkAsync(
                context,
                finalName,
                queriedType == DnsResourceRecordTypeEnum.DS,
                result.ZoneCutsTraversed.LastOrDefault(),
                $"The negative answer for '{finalName}'"
            );

        ZoneTrust trust = await GetZoneTrustAsync(context, zone);

        if (trust.State != DnssecValidationStateEnum.Secure)
            return FromNonSecure(trust);

        if (!VerifyDenialSignatures(context, result.DnssecRecords, trust.Keys, zone, out _))
            return DnssecValidationResult.Bogus(
                $"The NSEC or NSEC3 records proving the negative answer for '{finalName}' are missing or not signed by '{zone}'."
            );

        DenialOfExistenceOutcomeEnum denial = ProveNegative(
            finalName,
            queriedType,
            zone,
            result.Status == RecursiveResolutionStatusEnum.NoData,
            result.DnssecRecords
        );

        return denial switch
        {
            DenialOfExistenceOutcomeEnum.Proven => DnssecValidationResult.Secure(),

            // An Opt-Out span or an NSEC3 iteration count above the cap shows the name is not
            // secured by the zone: the answer is served without the AD bit.
            DenialOfExistenceOutcomeEnum.ProvenInsecure => DnssecValidationResult.Insecure(),
            _ => DnssecValidationResult.Bogus(
                $"The negative response's denial of existence for '{finalName}' did not verify."
            ),
        };
    }

    /// <summary>
    ///     This method verifies an RRset against the zone that signed it, or decides what the lack
    ///     of a signature means. A signature from an insecure zone makes the RRset insecure; a
    ///     wildcard expansion also needs its proof.
    /// </summary>
    /// <param name="context">The validation's shared state.</param>
    /// <param name="rrset">The RRset, all of one owner name and type.</param>
    /// <param name="dnssecRecords">The DNSSEC records that came with the RRset.</param>
    /// <param name="hintZone">
    ///     The zone the resolution found the RRset in, where the walk for an unsigned answer
    ///     starts; <see langword="null" /> for the root.
    /// </param>
    /// <param name="description">Names the RRset in a failure reason.</param>
    private async Task<DnssecValidationResult> ValidateRrsetAsync(
        ValidationContext context,
        IReadOnlyList<DnsResourceRecord> rrset,
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        DnsName hintZone,
        string description
    )
    {
        DnsName owner = rrset[0].Name;
        DnsResourceRecordTypeEnum type = rrset[0].Type;

        // A signer is the owner or an ancestor of it; a DS RRset belongs to the parent, so its
        // owner cannot sign it. Deepest first: the signing zone is the closest zone cut.
        List<DnsName> signers =
        [
            .. SignaturesOf(dnssecRecords, type)
                .Where(signature => signature.OwnerMatches(owner))
                .Select(signature => signature.Data.SignerName)
                .Where(signer =>
                    IsAtOrBelow(owner, signer)
                    && !(type == DnsResourceRecordTypeEnum.DS && signer == owner)
                )
                .Distinct()
                .OrderByDescending(signer => signer.Labels.Count)
                .Take(MaxSignersTried),
        ];

        if (signers.Count == 0)
            return await UnsignedWalkAsync(
                context,
                owner,
                type == DnsResourceRecordTypeEnum.DS,
                hintZone,
                description
            );

        string failure = "no signature verified";

        foreach (DnsName signer in signers)
        {
            ZoneTrust trust = await GetZoneTrustAsync(context, signer);

            if (trust.State == DnssecValidationStateEnum.Insecure)
                return DnssecValidationResult.Insecure();

            if (trust.State != DnssecValidationStateEnum.Secure)
            {
                failure = trust.Reason;
                continue;
            }

            RrsetVerificationResult verification = RrsetSignatureVerifier.Verify(
                rrset,
                dnssecRecords,
                trust.Keys,
                signer,
                context.Now,
                context.Signatures
            );

            if (!verification.IsVerified)
            {
                failure = verification.FailureReason;
                continue;
            }

            return CheckWildcardExpansion(
                context,
                owner,
                verification.VerifyingSignature,
                signer,
                trust,
                dnssecRecords,
                description
            );
        }

        return DnssecValidationResult.Bogus($"{description} did not verify: {failure}");
    }

    /// <summary>
    ///     This method requires the proof that no closer name exists when the verified RRSIG shows
    ///     the RRset was synthesized from a wildcard (its Labels field is below the owner's label
    ///     count): without it, an attacker could replay a wildcard answer for a name that exists
    ///     (RFC 4035 §5.3.4, RFC 5155 §8.8).
    /// </summary>
    private static DnssecValidationResult CheckWildcardExpansion(
        ValidationContext context,
        DnsName owner,
        RrSigRecordData signature,
        DnsName signer,
        ZoneTrust trust,
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        string description
    )
    {
        if (DnssecCanonicalization.ReconstructSignedOwner(owner, signature.Labels) == owner)
            return DnssecValidationResult.Secure();

        if (!VerifyDenialSignatures(context, dnssecRecords, trust.Keys, signer, out _))
            return DnssecValidationResult.Bogus(
                $"{description} is a wildcard expansion without a signed NSEC or NSEC3 proof that no closer name exists."
            );

        bool usesNsec3 = dnssecRecords.Any(record =>
            record.Type == DnsResourceRecordTypeEnum.Nsec3
        );
        DenialOfExistenceOutcomeEnum proof = usesNsec3
            ? DenialOfExistenceValidator.VerifyWildcardAnswerNsec3(
                owner,
                signature.Labels,
                signer,
                dnssecRecords
            )
            : DenialOfExistenceValidator.VerifyWildcardAnswer(
                owner,
                signature.Labels,
                signer,
                dnssecRecords
            );

        return proof switch
        {
            DenialOfExistenceOutcomeEnum.Proven => DnssecValidationResult.Secure(),
            DenialOfExistenceOutcomeEnum.ProvenInsecure => DnssecValidationResult.Insecure(),
            _ => DnssecValidationResult.Bogus(
                $"{description} is a wildcard expansion whose proof that no closer name exists did not verify."
            ),
        };
    }

    /// <summary>
    ///     This method decides what an RRset (or negative answer) without a signature is worth.
    ///     Starting at the closest known zone, it walks down toward the owner, one label at a
    ///     time, looking for a delegation that shows the owner is unsigned: a secure zone with a
    ///     secure child continues from the child; a signed proof of no DS at a delegation makes the
    ///     answer insecure. Reaching the owner while still in a secure zone means a signed zone
    ///     served unsigned data: bogus.
    /// </summary>
    /// <param name="context">The validation's shared state.</param>
    /// <param name="owner">The owner name of the unsigned data.</param>
    /// <param name="isDs">
    ///     Whether the data is a DS RRset or a DS negative answer; a DS belongs to the parent, so
    ///     the owner itself is not probed for a delegation.
    /// </param>
    /// <param name="hintZone">Where the resolution found the data; the root when not an ancestor.</param>
    /// <param name="description">Names the data in a failure reason.</param>
    private async Task<DnssecValidationResult> UnsignedWalkAsync(
        ValidationContext context,
        DnsName owner,
        bool isDs,
        DnsName hintZone,
        string description
    )
    {
        DnsName zone =
            hintZone is not null && IsAtOrBelow(owner, hintZone) && !(isDs && hintZone == owner)
                ? hintZone
                : DnsName.Root;
        ZoneTrust trust = await GetZoneTrustAsync(context, zone);
        int lastDepth = isDs ? owner.Labels.Count - 1 : owner.Labels.Count;

        for (int depth = zone.Labels.Count + 1; depth <= lastDepth; depth++)
        {
            if (trust.State != DnssecValidationStateEnum.Secure)
                break;

            DnsName candidate = Suffix(owner, depth);
            DelegationProbe probe = await ProbeDelegationAsync(context, candidate, zone, trust);

            if (probe.Trust is not null && probe.Trust.State == DnssecValidationStateEnum.Secure)
            {
                zone = candidate;
                trust = probe.Trust;
            }
            else if (!probe.IsNotADelegation)
            {
                trust = probe.Trust;

                break;
            }
        }

        return trust.State == DnssecValidationStateEnum.Secure
            ? DnssecValidationResult.Bogus(
                $"{description} has no signature, but its zone '{zone}' is signed and no unsigned delegation leads to it."
            )
            : FromNonSecure(trust);
    }

    /// <summary>
    ///     This method maps a zone result that is not secure to the validation result for data
    ///     below it.
    /// </summary>
    private static DnssecValidationResult FromNonSecure(ZoneTrust trust) =>
        trust.State switch
        {
            DnssecValidationStateEnum.Insecure => DnssecValidationResult.Insecure(),
            DnssecValidationStateEnum.Indeterminate => DnssecValidationResult.Indeterminate(
                trust.Reason
            ),
            _ => DnssecValidationResult.Bogus(trust.Reason),
        };

    /// <summary>
    ///     This method returns a zone's trust: from this validation's memo, from the shared
    ///     <see cref="DnssecTrustCache" />, or by building it from the root anchors or the
    ///     parent's state. Secure and insecure results are cached; failures are not.
    /// </summary>
    private async Task<ZoneTrust> GetZoneTrustAsync(ValidationContext context, DnsName zone)
    {
        if (context.Memo.TryGetValue(zone, out ZoneTrust memoized))
            return memoized;

        ZoneTrust cached = trustCache.TryGet(zone, context.Class, context.AnchorFingerprint);

        if (cached is not null)
        {
            context.Memo[zone] = cached;

            return cached;
        }

        ZoneTrust computed = zone.IsRoot
            ? await BuildRootTrustAsync(context)
            : await BuildChildTrustAsync(context, zone);

        context.Memo[zone] = computed;
        trustCache.Store(zone, context.Class, computed, context.AnchorFingerprint);

        return computed;
    }

    /// <summary>
    ///     This method builds the root's trust: its DNSKEY RRset must be signed by a key that
    ///     matches a trust anchor (RFC 4035 §5.2, RFC 5011 §4).
    /// </summary>
    private async Task<ZoneTrust> BuildRootTrustAsync(ValidationContext context)
    {
        IterativeResolutionResult dnsKeyResult = await FetchAsync(
            context,
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey
        );

        if (dnsKeyResult.Status != RecursiveResolutionStatusEnum.Success)
            return ZoneTrust.Bogus("No DNSKEY RRset could be retrieved for the root zone.");

        // Only DNSKEY records owned by the zone count; a CNAME or foreign record in the answer
        // is not part of the zone's key set.
        List<DnsResourceRecord> rootKeys = OwnedRecords(
            dnsKeyResult.Answer,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsName.Root
        );
        IReadOnlyList<DnsResourceRecord> anchorKeys =
            RrsetSignatureVerifier.FindKeysMatchingTrustAnchor(
                rootKeys,
                DnsName.Root,
                context.Anchors
            );

        if (anchorKeys.Count == 0)
            return ZoneTrust.Bogus(
                "The root zone's DNSKEY RRset matches none of this resolver's trust anchors."
            );

        RrsetVerificationResult verification = RrsetSignatureVerifier.Verify(
            rootKeys,
            dnsKeyResult.DnssecRecords,
            anchorKeys,
            DnsName.Root,
            context.Now,
            context.Signatures
        );

        return verification.IsVerified
            ? ZoneTrust.Secure(
                rootKeys,
                ExpiryOf(context, rootKeys, verification.VerifyingSignature)
            )
            : ZoneTrust.Bogus(
                $"The root zone's DNSKEY RRset did not verify against the trust anchor: {verification.FailureReason}"
            );
    }

    /// <summary>
    ///     This method builds the trust of a zone below the root from its DS lookup and its
    ///     parent's trust.
    /// </summary>
    private async Task<ZoneTrust> BuildChildTrustAsync(ValidationContext context, DnsName zone)
    {
        IterativeResolutionResult dsResult = await FetchAsync(
            context,
            zone,
            DnsResourceRecordTypeEnum.DS
        );

        if (
            dsResult.Status
            is not (
                RecursiveResolutionStatusEnum.Success
                or RecursiveResolutionStatusEnum.NoData
                or RecursiveResolutionStatusEnum.NxDomain
            )
        )
            return ZoneTrust.Bogus($"Could not obtain a DS answer for '{zone}'.");

        DnsName parent = DeriveParent(zone, dsResult);
        ZoneTrust parentTrust = await GetZoneTrustAsync(context, parent);

        // A zone inherits an insecure, bogus or indeterminate parent: no secure path reaches it.
        if (parentTrust.State != DnssecValidationStateEnum.Secure)
            return parentTrust.State == DnssecValidationStateEnum.Insecure
                ? ZoneTrust.Insecure(parentTrust.ExpiresAtUtc)
                : parentTrust;

        DelegationProbe probe = await EvaluateDsAsync(context, zone, parent, parentTrust, dsResult);

        // A secure parent that proves the name is not a delegation contradicts a zone claiming
        // to start there; accepting it would be the downgrade RFC 6840 §4.4 warns about.
        return probe.IsNotADelegation
            ? ZoneTrust.Bogus(
                $"'{parent}' proves '{zone}' is not a delegation, yet answers are signed as if it were a zone."
            )
            : probe.Trust;
    }

    /// <summary>
    ///     This record is the outcome of looking at a name's DS in a secure parent: the name is a
    ///     zone (secure, insecure or bogus), or it is not a delegation at all.
    /// </summary>
    private sealed record DelegationProbe(ZoneTrust Trust, bool IsNotADelegation);

    /// <summary>
    ///     This method fetches the DS of a name below a secure zone and evaluates it, for the
    ///     unsigned walk.
    /// </summary>
    private async Task<DelegationProbe> ProbeDelegationAsync(
        ValidationContext context,
        DnsName name,
        DnsName parent,
        ZoneTrust parentTrust
    )
    {
        IterativeResolutionResult dsResult = await FetchAsync(
            context,
            name,
            DnsResourceRecordTypeEnum.DS
        );

        return await EvaluateDsAsync(context, name, parent, parentTrust, dsResult);
    }

    /// <summary>
    ///     This method evaluates the DS answer for <paramref name="name" /> against its secure
    ///     parent <paramref name="parent" /> (RFC 4035 §5.2). A signed DS RRset leads to the child's
    ///     DNSKEY RRset, which a DS-matching key must sign. A signed NSEC or NSEC3 proof of no DS at
    ///     a delegation makes the child insecure. A proof that the name exists but is not a
    ///     delegation is reported as such, and NXDOMAIN or a failed lookup is bogus.
    /// </summary>
    private async Task<DelegationProbe> EvaluateDsAsync(
        ValidationContext context,
        DnsName name,
        DnsName parent,
        ZoneTrust parentTrust,
        IterativeResolutionResult dsResult
    )
    {
        switch (dsResult.Status)
        {
            case RecursiveResolutionStatusEnum.Success:
                return new DelegationProbe(
                    await BuildFromDsRecordsAsync(context, name, parent, parentTrust, dsResult),
                    false
                );

            case RecursiveResolutionStatusEnum.NoData:
                break;

            case RecursiveResolutionStatusEnum.NxDomain:
                // A name that holds a zone, or has records below it, exists: NXDOMAIN for its DS
                // (RFC 4035 §3.1.4.1) contradicts the data under test.
                return new DelegationProbe(
                    ZoneTrust.Bogus($"'{parent}' answered NXDOMAIN for the DS of '{name}'."),
                    false
                );

            default:
                return new DelegationProbe(
                    ZoneTrust.Bogus($"Could not obtain a DS answer for '{name}'."),
                    false
                );
        }

        if (
            !VerifyDenialSignatures(
                context,
                dsResult.DnssecRecords,
                parentTrust.Keys,
                parent,
                out DateTimeOffset proofExpiry
            )
        )
            return new DelegationProbe(
                ZoneTrust.Bogus(
                    $"No DS record exists for '{name}', and its absence is not proven by NSEC or NSEC3 records signed by '{parent}'."
                ),
                false
            );

        DenialOfExistenceOutcomeEnum noDs = ProveNegative(
            name,
            DnsResourceRecordTypeEnum.DS,
            parent,
            true,
            dsResult.DnssecRecords
        );

        if (
            noDs
            is DenialOfExistenceOutcomeEnum.Proven
                or DenialOfExistenceOutcomeEnum.ProvenInsecure
        )
            return new DelegationProbe(
                ZoneTrust.Insecure(Earliest(parentTrust.ExpiresAtUtc, proofExpiry)),
                false
            );

        // Not a delegation without a DS: is the name an ordinary name of the parent zone? An NS
        // query asks the same bitmap question and refuses the parent side of a delegation.
        return
            ProveNegative(name, DnsResourceRecordTypeEnum.NS, parent, true, dsResult.DnssecRecords)
            == DenialOfExistenceOutcomeEnum.Proven
            ? new DelegationProbe(null, true)
            : new DelegationProbe(
                ZoneTrust.Bogus($"The proof of no DS for '{name}' did not verify."),
                false
            );
    }

    /// <summary>
    ///     This method builds a zone's trust from a positive DS answer: the DS RRset is verified
    ///     with the parent's keys, a DS set with nothing usable makes the zone insecure
    ///     (RFC 4035 §5.2), and otherwise a DNSKEY that matches a DS must sign the zone's DNSKEY
    ///     RRset.
    /// </summary>
    private async Task<ZoneTrust> BuildFromDsRecordsAsync(
        ValidationContext context,
        DnsName zone,
        DnsName parent,
        ZoneTrust parentTrust,
        IterativeResolutionResult dsResult
    )
    {
        // A CNAME or foreign record in the answer is not part of the DS RRset.
        List<DnsResourceRecord> dsRrset = OwnedRecords(
            dsResult.Answer,
            DnsResourceRecordTypeEnum.DS,
            zone
        );

        if (dsRrset.Count == 0)
            return ZoneTrust.Bogus($"The answer for the DS of '{zone}' holds no DS record.");

        RrsetVerificationResult dsVerification = RrsetSignatureVerifier.Verify(
            dsRrset,
            dsResult.DnssecRecords,
            parentTrust.Keys,
            parent,
            context.Now,
            context.Signatures
        );

        if (!dsVerification.IsVerified)
            return ZoneTrust.Bogus(
                $"The DS RRset for '{zone}' did not verify against '{parent}''s trusted keys: {dsVerification.FailureReason}"
            );

        DateTimeOffset expiry = Earliest(
            parentTrust.ExpiresAtUtc,
            ExpiryOf(context, dsRrset, dsVerification.VerifyingSignature)
        );

        // RFC 4035 §5.2: a DS RRset with no digest type or algorithm this validator supports
        // makes the zone Insecure, not Bogus.
        if (!RrsetSignatureVerifier.HasSupportedDs(dsRrset))
            return ZoneTrust.Insecure(expiry);

        IterativeResolutionResult dnsKeyResult = await FetchAsync(
            context,
            zone,
            DnsResourceRecordTypeEnum.DnsKey
        );

        if (dnsKeyResult.Status != RecursiveResolutionStatusEnum.Success)
            return ZoneTrust.Bogus(
                $"'{zone}' has a DS record, but no DNSKEY RRset could be retrieved for it."
            );

        List<DnsResourceRecord> zoneKeys = OwnedRecords(
            dnsKeyResult.Answer,
            DnsResourceRecordTypeEnum.DnsKey,
            zone
        );
        IReadOnlyList<DnsResourceRecord> matchingKeys = RrsetSignatureVerifier.FindKeysMatchingDs(
            zoneKeys,
            zone,
            dsRrset
        );

        if (matchingKeys.Count == 0)
            return ZoneTrust.Bogus($"No DNSKEY at '{zone}' matches its own DS record.");

        RrsetVerificationResult keysVerification = RrsetSignatureVerifier.Verify(
            zoneKeys,
            dnsKeyResult.DnssecRecords,
            matchingKeys,
            zone,
            context.Now,
            context.Signatures
        );

        return keysVerification.IsVerified
            ? ZoneTrust.Secure(
                zoneKeys,
                Earliest(expiry, ExpiryOf(context, zoneKeys, keysVerification.VerifyingSignature))
            )
            : ZoneTrust.Bogus(
                $"The DNSKEY RRset for '{zone}' did not verify: {keysVerification.FailureReason}"
            );
    }

    /// <summary>
    ///     This method finds the zone that holds a DS answer, which is the parent of the zone it
    ///     is for. The best evidence is the signer of the DS, NSEC or NSEC3 RRSIGs, then the SOA
    ///     owner of a negative answer, then the last zone cut of the resolution; the name minus its
    ///     leftmost label is the last resort. Each candidate must be a proper ancestor.
    ///     A wrong guess is harmless: the DS must verify with the parent's own keys.
    /// </summary>
    private static DnsName DeriveParent(DnsName zone, IterativeResolutionResult dsResult)
    {
        DnsName bySigner = SignaturesOf(
                dsResult.DnssecRecords,
                DnsResourceRecordTypeEnum.DS,
                DnsResourceRecordTypeEnum.Nsec,
                DnsResourceRecordTypeEnum.Nsec3,
                DnsResourceRecordTypeEnum.Soa
            )
            .Select(signature => signature.Data.SignerName)
            .Where(signer => IsProperAncestor(signer, zone))
            .OrderByDescending(signer => signer.Labels.Count)
            .FirstOrDefault();

        if (bySigner is not null)
            return bySigner;

        if (dsResult.Soa is not null && IsProperAncestor(dsResult.Soa.Name, zone))
            return dsResult.Soa.Name;

        DnsName lastCut = dsResult.ZoneCutsTraversed.LastOrDefault();

        return lastCut is not null && IsProperAncestor(lastCut, zone)
            ? lastCut
            : Suffix(zone, zone.Labels.Count - 1);
    }

    /// <summary>
    ///     This method verifies the signature of every NSEC and NSEC3 RRset that lies in the
    ///     zone against the zone's keys, so an unsigned record cannot prove anything. Records
    ///     outside the zone are ignored here, as the proof checks ignore them. It fails when there
    ///     are none.
    /// </summary>
    /// <param name="context">The validation's shared state: clock and signature budget.</param>
    /// <param name="dnssecRecords">The DNSSEC records of the response.</param>
    /// <param name="trustedKeys">The zone's validated DNSKEY RRset.</param>
    /// <param name="zoneApex">The zone whose keys must have signed the records.</param>
    /// <param name="earliestExpiry">
    ///     The earliest expiry among the verifying signatures and record TTLs, for the trust cache.
    /// </param>
    private static bool VerifyDenialSignatures(
        ValidationContext context,
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        IReadOnlyList<DnsResourceRecord> trustedKeys,
        DnsName zoneApex,
        out DateTimeOffset earliestExpiry
    )
    {
        bool foundAny = false;
        earliestExpiry = DateTimeOffset.MaxValue;

        foreach (
            var group in dnssecRecords
                .Where(record =>
                    record.Type is DnsResourceRecordTypeEnum.Nsec or DnsResourceRecordTypeEnum.Nsec3
                    && IsAtOrBelow(record.Name, zoneApex)
                )
                .GroupBy(record => (record.Name, record.Type))
        )
        {
            foundAny = true;
            List<DnsResourceRecord> rrset = [.. group];
            RrsetVerificationResult verification = RrsetSignatureVerifier.Verify(
                rrset,
                dnssecRecords,
                trustedKeys,
                zoneApex,
                context.Now,
                context.Signatures
            );

            if (!verification.IsVerified)
                return false;

            earliestExpiry = Earliest(
                earliestExpiry,
                ExpiryOf(context, rrset, verification.VerifyingSignature)
            );
        }

        return foundAny;
    }

    /// <summary>
    ///     This method runs the NSEC or NSEC3 proof for a negative answer, choosing the scheme by
    ///     which record type the response carries.
    /// </summary>
    private static DenialOfExistenceOutcomeEnum ProveNegative(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsName zoneApex,
        bool isNoData,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        bool usesNsec3 = dnssecRecords.Any(record =>
            record.Type == DnsResourceRecordTypeEnum.Nsec3
        );

        if (isNoData)
            return usesNsec3
                ? DenialOfExistenceValidator.VerifyNoDataNsec3(name, type, zoneApex, dnssecRecords)
                : DenialOfExistenceValidator.VerifyNoData(name, type, zoneApex, dnssecRecords);

        return usesNsec3
            ? DenialOfExistenceValidator.VerifyNxDomainNsec3(name, zoneApex, dnssecRecords)
            : DenialOfExistenceValidator.VerifyNxDomain(name, zoneApex, dnssecRecords);
    }

    /// <summary>
    ///     This method fetches one RRset through the validation's fetcher.
    /// </summary>
    private static Task<IterativeResolutionResult> FetchAsync(
        ValidationContext context,
        DnsName name,
        DnsResourceRecordTypeEnum type
    ) =>
        context.Fetcher.FetchAsync(
            name,
            type,
            context.Class,
            context.Budget,
            context.StoppingToken
        );

    /// <summary>
    ///     This record is a decoded RRSIG with the owner name of the record that carried it.
    /// </summary>
    private sealed record OwnedSignature(DnsName Owner, RrSigRecordData Data)
    {
        public bool OwnerMatches(DnsName owner) => Owner == owner;
    }

    /// <summary>
    ///     This method decodes the RRSIGs among the DNSSEC records that cover one of the types.
    /// </summary>
    private static IEnumerable<OwnedSignature> SignaturesOf(
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        params DnsResourceRecordTypeEnum[] coveredTypes
    )
    {
        foreach (DnsResourceRecord record in dnssecRecords)
        {
            if (
                record.Type == DnsResourceRecordTypeEnum.RrSig
                && DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, record.Data)
                    is RrSigRecordData data
                && coveredTypes.Contains(data.TypeCovered)
            )
                yield return new OwnedSignature(record.Name, data);
        }
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
    ///     This method computes when a verified RRset stops being usable: its smallest TTL, the
    ///     RRSIG's Original TTL (RFC 4035 §5.3.3) and the RRSIG's expiration, whichever is first.
    /// </summary>
    private static DateTimeOffset ExpiryOf(
        ValidationContext context,
        IReadOnlyList<DnsResourceRecord> rrset,
        RrSigRecordData signature
    )
    {
        uint ttl = Math.Min(
            rrset.Min(record => record.TimeToLive),
            signature.OriginalTimeToLiveSeconds
        );

        return Earliest(
            context.Now.AddSeconds(ttl),
            DateTimeOffset.FromUnixTimeSeconds(signature.SignatureExpiration)
        );
    }

    private static DateTimeOffset Earliest(DateTimeOffset left, DateTimeOffset right) =>
        left < right ? left : right;

    /// <summary>
    ///     This method summarizes a trust anchor set into a string that changes whenever the set
    ///     does, for <see cref="DnssecTrustCache" />.
    /// </summary>
    private static string AnchorFingerprint(IReadOnlyList<TrustAnchor> anchors) =>
        string.Join(
            ';',
            anchors
                .Select(anchor =>
                    $"{anchor.KeyTag}/{(int)anchor.Algorithm}/{anchor.DigestType}/{Convert.ToHexString(anchor.Digest)}"
                )
                .Order(StringComparer.Ordinal)
        );

    /// <summary>
    ///     This method tells whether a name equals an ancestor or is below it.
    /// </summary>
    private static bool IsAtOrBelow(DnsName name, DnsName ancestor) =>
        name.Labels.Count >= ancestor.Labels.Count
        && Suffix(name, ancestor.Labels.Count) == ancestor;

    /// <summary>
    ///     This method tells whether <paramref name="ancestor" /> is above <paramref name="name" />,
    ///     not equal to it.
    /// </summary>
    private static bool IsProperAncestor(DnsName ancestor, DnsName name) =>
        ancestor.Labels.Count < name.Labels.Count && IsAtOrBelow(name, ancestor);

    /// <summary>
    ///     This method returns the rightmost labels of a name.
    /// </summary>
    private static DnsName Suffix(DnsName name, int labelCount) =>
        new([.. name.Labels.Skip(name.Labels.Count - labelCount)]);
}
