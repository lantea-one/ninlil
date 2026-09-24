using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class implements <see cref="IZoneSigningService" />: the offline signing pipeline
///     that reads a zone's stored records fresh from the database and computes every piece of
///     DNSSEC content RFC 4034/5155 require for it -- the DNSKEY RRset, an RRSIG over every
///     signed RRset, the zone's complete NSEC or NSEC3 chain, and any DS records this zone must
///     generate for its own delegations to other, signed zones this same server also hosts.
/// </summary>
/// <param name="zoneRepository">The repository this service reads zones from.</param>
/// <param name="recordRepository">The repository this service reads a zone's own records from.</param>
/// <param name="keyRepository">The repository this service reads a zone's DNSSEC keys from.</param>
/// <param name="keyManagement">The service this service decrypts a key's private key material through.</param>
/// <param name="options">
///     This service's own signature-timing configuration, read fresh
///     (<see cref="IOptionsMonitor{TOptions}.CurrentValue" />) on every signing call rather than
///     snapshotted once -- this service is registered scoped, but the plain
///     <see cref="IOptions{TOptions}" /> a scoped registration would otherwise receive is itself
///     backed by a singleton whose <c>.Value</c> is computed once and cached for the life of the
///     process, so a configuration-file write would never have been observed at all without this
///     -- the same <see cref="IOptions{TOptions}" />-vs-<see cref="IOptionsMonitor{TOptions}" />
///     staleness trap several other hot-reloadable options classes in this codebase guard against
///     the same way.
/// </param>
public class ZoneSigningService(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IDnssecKeyRepository keyRepository,
    IDnssecKeyManagementService keyManagement,
    IOptionsMonitor<ZoneSigningOptions> options
) : IZoneSigningService
{
    /// <summary>
    ///     This constant contains the DS digest algorithm this service always generates for a
    ///     delegation -- SHA-256, per IANA's "Delegation Signer (DS) Resource Record (RR) Type
    ///     Digest Algorithms" registry, RFC 8624's mandatory-to-implement choice, chosen over the
    ///     legacy SHA-1 option this project could equally well produce.
    /// </summary>
    private const byte DsDigestType = 2;

    /// <inheritdoc />
    public async Task<SignedZoneData> SignZoneAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    )
    {
        ZoneEntity zone =
            await zoneRepository.GetByIdAsync(zoneId, stoppingToken)
            ?? throw new InvalidOperationException($"No zone with id '{zoneId}' exists.");

        if (!zone.IsDnssecSigned)
            throw new InvalidOperationException(
                $"Zone '{zone.Name}' is not marked as DNSSEC-signed."
            );

        DnsName apexName = DnsName.Parse(zone.Name);

        // "Published" here means every key still on the zone's own DNSKEY RRset at all -- every
        // DnssecKeyLifecycleStateEnum value except Removed, which is the one state RFC 6781 itself
        // describes as full withdrawal ("DNSKEY removal"). This deliberately includes a key that
        // is not currently signing anything (Published, mid pre-roll wait; or Retired, kept
        // published only so an already-cached signature it produced can still be verified) --
        // excluding either from this DNSKEY RRset would defeat the entire point of RFC 6781's own
        // rollover choreography.
        List<DnssecKeyEntity> activeKeys =
        [
            .. (await keyRepository.ListByZoneIdAsync(zoneId, stoppingToken)).Where(key =>
                key.LifecycleState != DnssecKeyLifecycleStateEnum.Removed
            ),
        ];

        if (activeKeys.Count == 0)
            throw new InvalidOperationException(
                $"Zone '{zone.Name}' is marked signed but has no active DNSSEC key."
            );

        // A key actually *signs* something only in Active or DoubleSignature -- Published (not
        // trusted enough yet) and Retired (stepping down, kept published only for verification
        // continuity) never produce a new signature, per this enumeration's own doc comment.
        static bool Signs(DnssecKeyEntity key) =>
            key.LifecycleState
                is DnssecKeyLifecycleStateEnum.Active
                    or DnssecKeyLifecycleStateEnum.DoubleSignature;

        List<DnssecKeyEntity> activeKsks =
        [
            .. activeKeys.Where(key => key.Role == DnssecKeyRoleEnum.Ksk && Signs(key)),
        ];

        // A ZSK signs every ordinary RRset; a KSK signs only the DNSKEY RRset, per RFC 6781's
        // conventional split. When a zone has generated only a KSK (a minimal, non-idiomatic but
        // valid configuration), that same key signs ordinary data too, rather than leaving the
        // zone marked signed with nothing in it actually verifiable.
        List<DnssecKeyEntity> ordinarySigners =
        [
            .. activeKeys.Where(key => key.Role == DnssecKeyRoleEnum.Zsk && Signs(key)),
        ];
        if (ordinarySigners.Count == 0)
            ordinarySigners = [.. activeKeys.Where(Signs)];

        List<DnssecKeyEntity> dnsKeySigners =
            activeKsks.Count > 0 ? activeKsks : [.. activeKeys.Where(Signs)];

        IReadOnlyList<RecordEntity> storedRecords = await recordRepository.ListByZoneIdAsync(
            zoneId,
            stoppingToken
        );
        Dictionary<DnsName, List<RecordEntity>> recordsByName = storedRecords
            .Where(record => !record.IsDisabled)
            .GroupBy(record => DnsName.Parse(record.Name))
            .ToDictionary(group => group.Key, group => group.ToList());

        // A delegation point is any non-apex name carrying an NS record -- the parent zone's own
        // NS records at its own apex describe who is authoritative for *this* zone, not a
        // delegation of it elsewhere, so the apex itself is deliberately excluded.
        HashSet<DnsName> delegationPoints =
        [
            .. recordsByName
                .Where(pair =>
                    pair.Key != apexName
                    && pair.Value.Any(record => record.Type == DnsResourceRecordTypeEnum.NS)
                )
                .Select(pair => pair.Key),
        ];

        Dictionary<DnsName, List<DnsResourceRecord>> generatedDs =
            await ComputeDelegationDsRecordsAsync(delegationPoints, stoppingToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        uint inception = (uint)(now - options.CurrentValue.InceptionOffset).ToUnixTimeSeconds();
        uint expiration = (uint)
            (now + options.CurrentValue.SignatureValidityPeriod).ToUnixTimeSeconds();

        Dictionary<
            (DnsName Name, DnsResourceRecordTypeEnum Type),
            IReadOnlyList<DnsResourceRecord>
        > rrsigs = [];

        // The DNSKEY RRset itself.
        uint dnsKeyTtl = storedRecords
            .Where(record => record.Type == DnsResourceRecordTypeEnum.Soa)
            .Select(record => record.TimeToLiveSeconds)
            .DefaultIfEmpty(3600u)
            .First();
        List<DnsResourceRecord> dnsKeyRecords =
        [
            .. activeKeys.Select(key => new DnsResourceRecord(
                apexName,
                DnsResourceRecordTypeEnum.DnsKey,
                DnsClassEnum.Internet,
                dnsKeyTtl,
                EncodeDnsKeyRdata(key)
            )),
        ];
        SignRrset(
            apexName,
            apexName,
            DnsResourceRecordTypeEnum.DnsKey,
            dnsKeyRecords,
            dnsKeySigners,
            inception,
            expiration,
            dnsKeyTtl,
            rrsigs
        );

        // Every ordinary RRset, plus each delegation's generated DS RRset.
        foreach ((DnsName name, List<RecordEntity> recordsAtName) in recordsByName)
        {
            bool isDelegation = delegationPoints.Contains(name);

            foreach (
                DnsResourceRecordTypeEnum type in recordsAtName
                    .Select(record => record.Type)
                    .Distinct()
            )
            {
                // The SOA is always at the apex and is already covered by the ordinary path
                // below like any other type; NS at a delegation point is the child's own
                // authoritative data, never signed by the parent (RFC 4035 §2.2), and any glue
                // stored alongside it belongs to the child too, so nothing else at a delegation
                // point is eligible either.
                if (isDelegation)
                    continue;

                List<RecordEntity> recordsOfType =
                [
                    .. recordsAtName.Where(record => record.Type == type),
                ];
                uint ttl = recordsOfType[0].TimeToLiveSeconds;
                List<DnsResourceRecord> wireRecords =
                [
                    .. recordsOfType.Select(record => RecordEntityCodec.ToWireRecord(record, name)),
                ];

                SignRrset(
                    apexName,
                    name,
                    type,
                    wireRecords,
                    ordinarySigners,
                    inception,
                    expiration,
                    ttl,
                    rrsigs
                );
            }

            if (
                isDelegation && generatedDs.TryGetValue(name, out List<DnsResourceRecord> dsRecords)
            )
                SignRrset(
                    apexName,
                    name,
                    DnsResourceRecordTypeEnum.DS,
                    dsRecords,
                    ordinarySigners,
                    inception,
                    expiration,
                    dnsKeyTtl,
                    rrsigs
                );
        }

        List<DnsName> signableNames = ComputeSignableOwnerNames(apexName, recordsByName);

        (List<DnsResourceRecord> denialRecords, DnsResourceRecord nsec3ParamRecord) =
            zone.DenialOfExistenceMode == DnssecDenialOfExistenceModeEnum.Nsec3
                ? BuildNsec3Chain(
                    zone,
                    apexName,
                    signableNames,
                    recordsByName,
                    delegationPoints,
                    generatedDs,
                    ordinarySigners,
                    inception,
                    expiration,
                    dnsKeyTtl,
                    rrsigs
                )
                : (
                    BuildNsecChain(
                        apexName,
                        signableNames,
                        recordsByName,
                        delegationPoints,
                        generatedDs,
                        ordinarySigners,
                        inception,
                        expiration,
                        dnsKeyTtl,
                        rrsigs
                    ),
                    null
                );

        return new SignedZoneData(
            dnsKeyRecords,
            rrsigs,
            denialRecords,
            nsec3ParamRecord,
            generatedDs.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<DnsResourceRecord>)pair.Value
            ),
            DateTimeOffset.FromUnixTimeSeconds(expiration)
        );
    }

    /// <summary>
    ///     This method computes the DS records this zone must generate for each delegation that
    ///     points at another zone this same server also hosts and has itself signed -- the one
    ///     piece of a child's chain of trust that only its parent zone can publish, since the DS
    ///     record's whole purpose is to be found *outside* the zone it vouches for.
    /// </summary>
    private async Task<
        Dictionary<DnsName, List<DnsResourceRecord>>
    > ComputeDelegationDsRecordsAsync(
        IReadOnlyCollection<DnsName> delegationPoints,
        CancellationToken stoppingToken
    )
    {
        Dictionary<DnsName, List<DnsResourceRecord>> result = [];

        foreach (DnsName delegationPoint in delegationPoints)
        {
            string delegationName = delegationPoint.ToString().TrimEnd('.');
            ZoneEntity childZone = (
                await zoneRepository.ListAsync(zone => zone.Name == delegationName, stoppingToken)
            ).FirstOrDefault();

            if (childZone is not { IsDnssecSigned: true })
                continue;

            IReadOnlyList<DnssecKeyEntity> childKeys = await keyRepository.ListByZoneIdAsync(
                childZone.Id,
                stoppingToken
            );

            // Exactly one DS per delegation, naming whichever KSK is currently canonical
            // (LifecycleState.Active) -- RFC 6781 §4.1.2/Figure 4's own Double-Signature KSK
            // rollover keeps a single DS record at the parent throughout, swapped atomically at
            // the "DS change" stage, rather than publishing a DS for both keys of a rollover pair
            // simultaneously. A KSK still in DoubleSignature (either a rollover's own incoming key,
            // not yet promoted, or its outgoing predecessor, already displaced) deliberately gets
            // no DS of its own here -- DnssecKeyRolloverBackgroundService is the one path that
            // actually promotes a KSK to Active, and does so only once this delegation's own DS
            // has had time to propagate.
            List<DnsResourceRecord> dsRecords =
            [
                .. childKeys
                    .Where(key =>
                        key.LifecycleState == DnssecKeyLifecycleStateEnum.Active
                        && key.Role == DnssecKeyRoleEnum.Ksk
                    )
                    .Select(childKsk =>
                    {
                        byte[] childDnsKeyRdata = EncodeDnsKeyRdata(childKsk);
                        byte[] digest = DsDigestCodec.Compute(
                            DsDigestType,
                            delegationPoint,
                            childDnsKeyRdata
                        );

                        return new DnsResourceRecord(
                            delegationPoint,
                            DnsResourceRecordTypeEnum.DS,
                            DnsClassEnum.Internet,
                            3600,
                            DnsRecordDataCodecRegistry.Encode(
                                DnsResourceRecordTypeEnum.DS,
                                new DsRecordData(
                                    childKsk.KeyTag,
                                    childKsk.Algorithm,
                                    DsDigestType,
                                    digest
                                )
                            )
                        );
                    }),
            ];

            if (dsRecords.Count > 0)
                result[delegationPoint] = dsRecords;
        }

        return result;
    }

    /// <summary>
    ///     This method signs a single RRset with every one of a set of keys, adding the result to
    ///     <paramref name="rrsigs" /> under the RRset's own (name, type) key. An RRset with no
    ///     records, or a set of signers with none actually registered in
    ///     <see cref="DnssecAlgorithmRegistry" />, produces no signatures at all.
    /// </summary>
    private void SignRrset(
        DnsName apexName,
        DnsName ownerName,
        DnsResourceRecordTypeEnum typeCovered,
        IReadOnlyList<DnsResourceRecord> rrset,
        IReadOnlyList<DnssecKeyEntity> signers,
        uint inception,
        uint expiration,
        uint originalTtl,
        Dictionary<
            (DnsName Name, DnsResourceRecordTypeEnum Type),
            IReadOnlyList<DnsResourceRecord>
        > rrsigs
    )
    {
        if (rrset.Count == 0)
            return;

        byte labels = (byte)(ownerName.Labels.Count - (IsWildcardOwner(ownerName) ? 1 : 0));
        List<DnsResourceRecord> signatures = [];

        foreach (DnssecKeyEntity signer in signers)
        {
            if (
                !DnssecAlgorithmRegistry.TryGetAlgorithm(
                    signer.Algorithm,
                    out IDnssecSigningAlgorithm algorithm
                )
            )
                continue;

            RrSigRecordData unsignedMetadata = new(
                typeCovered,
                signer.Algorithm,
                labels,
                originalTtl,
                expiration,
                inception,
                signer.KeyTag,
                apexName,
                []
            );
            byte[] signingInput = DnssecCanonicalization.BuildSigningInput(
                unsignedMetadata,
                ownerName,
                DnsClassEnum.Internet,
                rrset
            );
            byte[] signature = algorithm.Sign(
                signingInput,
                keyManagement.DecryptPrivateKeyPkcs8(signer)
            );

            RrSigRecordData signedMetadata = new(
                typeCovered,
                signer.Algorithm,
                labels,
                originalTtl,
                expiration,
                inception,
                signer.KeyTag,
                unsignedMetadata.SignerName,
                signature
            );
            signatures.Add(
                new DnsResourceRecord(
                    ownerName,
                    DnsResourceRecordTypeEnum.RrSig,
                    DnsClassEnum.Internet,
                    originalTtl,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.RrSig,
                        signedMetadata
                    )
                )
            );
        }

        if (signatures.Count > 0)
            rrsigs[(ownerName, typeCovered)] = signatures;
    }

    /// <summary>
    ///     This method checks whether a name's own leftmost label is literally the wildcard
    ///     label, per RFC 4034 §3.1.3's rule that an RRSIG's Labels field must not count it.
    /// </summary>
    private static bool IsWildcardOwner(DnsName name) =>
        name.Labels.Count > 0 && name.Labels[0] is [(byte)'*'];

    /// <summary>
    ///     This method builds every owner name that needs its own NSEC or NSEC3 record: every
    ///     name that actually has stored records, plus every ancestor of one of those names, up
    ///     to but excluding the zone apex, that has no records of its own -- an "empty
    ///     non-terminal" per RFC 1035 §3.1/RFC 2308 §5, which still needs a signed denial-of-
    ///     existence record proving it has no data even though it is not itself nonexistent.
    /// </summary>
    private static List<DnsName> ComputeSignableOwnerNames(
        DnsName apexName,
        Dictionary<DnsName, List<RecordEntity>> recordsByName
    )
    {
        HashSet<DnsName> names = [.. recordsByName.Keys];

        foreach (DnsName name in recordsByName.Keys)
            for (int start = 1; name.Labels.Count - start >= apexName.Labels.Count; start++)
                names.Add(new DnsName([.. name.Labels.Skip(start)]));

        return [.. names];
    }

    /// <summary>
    ///     This method computes the set of record types a signed name's own NSEC/NSEC3 type
    ///     bitmap should list, excluding a delegation point's unsigned NS/glue data down to just
    ///     NS itself (still listed as *present*, even though it carries no RRSIG of its own), per
    ///     RFC 4035 §2.2.
    /// </summary>
    private static List<DnsResourceRecordTypeEnum> ComputePresentTypes(
        DnsName name,
        Dictionary<DnsName, List<RecordEntity>> recordsByName,
        HashSet<DnsName> delegationPoints,
        Dictionary<DnsName, List<DnsResourceRecord>> generatedDs
    )
    {
        List<DnsResourceRecordTypeEnum> types = [];

        if (recordsByName.TryGetValue(name, out List<RecordEntity> recordsAtName))
        {
            types.AddRange(
                delegationPoints.Contains(name)
                    ? [DnsResourceRecordTypeEnum.NS]
                    : recordsAtName.Select(record => record.Type).Distinct()
            );
        }

        if (generatedDs.ContainsKey(name))
            types.Add(DnsResourceRecordTypeEnum.DS);

        return types;
    }

    /// <summary>
    ///     This method builds a zone's complete NSEC chain per RFC 4034 §4: one record per
    ///     signable owner name, in canonical order, each naming the very next one -- wrapping
    ///     back to the first at the end, per §4.1.1's "circular list" rule -- and each signed the
    ///     same way any other RRset is.
    /// </summary>
    private List<DnsResourceRecord> BuildNsecChain(
        DnsName apexName,
        List<DnsName> signableNames,
        Dictionary<DnsName, List<RecordEntity>> recordsByName,
        HashSet<DnsName> delegationPoints,
        Dictionary<DnsName, List<DnsResourceRecord>> generatedDs,
        IReadOnlyList<DnssecKeyEntity> signers,
        uint inception,
        uint expiration,
        uint ttl,
        Dictionary<
            (DnsName Name, DnsResourceRecordTypeEnum Type),
            IReadOnlyList<DnsResourceRecord>
        > rrsigs
    )
    {
        List<DnsName> ordered =
        [
            .. signableNames.OrderBy(
                name => name,
                Comparer<DnsName>.Create(DnssecCanonicalization.CompareCanonicalOrder)
            ),
        ];
        List<DnsResourceRecord> records = [];

        for (int i = 0; i < ordered.Count; i++)
        {
            DnsName name = ordered[i];
            DnsName next = ordered[(i + 1) % ordered.Count];

            // RFC 4035 §2.3 requires every NSEC record's own type, and its own covering RRSIG,
            // to appear in its type bitmap unconditionally -- unlike every other type there,
            // neither depends on anything else actually being signed at this name.
            List<DnsResourceRecordTypeEnum> types =
            [
                .. ComputePresentTypes(name, recordsByName, delegationPoints, generatedDs),
                DnsResourceRecordTypeEnum.Nsec,
                DnsResourceRecordTypeEnum.RrSig,
            ];

            DnsResourceRecord nsecRecord = new(
                name,
                DnsResourceRecordTypeEnum.Nsec,
                DnsClassEnum.Internet,
                ttl,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Nsec,
                    new NsecRecordData(next, types)
                )
            );
            records.Add(nsecRecord);

            SignRrset(
                apexName,
                name,
                DnsResourceRecordTypeEnum.Nsec,
                [nsecRecord],
                signers,
                inception,
                expiration,
                ttl,
                rrsigs
            );
        }

        return records;
    }

    /// <summary>
    ///     This method builds a zone's complete NSEC3 chain per RFC 5155 §7.1: one record per
    ///     signable owner name's salted, iterated hash, in ascending hash order, each naming the
    ///     very next hash -- wrapping back to the first at the end, the same as the plain NSEC
    ///     chain does with plain names. Every generated record's own Opt-Out flag (RFC 5155
    ///     §3.1.2.1) is set together, zone-wide, whenever this zone has at least one insecure
    ///     delegation -- see this method's own <c>useOptOut</c> local for why that is a whole-zone
    ///     decision, not a per-record one.
    /// </summary>
    private (List<DnsResourceRecord> Records, DnsResourceRecord Nsec3ParamRecord) BuildNsec3Chain(
        ZoneEntity zone,
        DnsName apexName,
        List<DnsName> signableNames,
        Dictionary<DnsName, List<RecordEntity>> recordsByName,
        HashSet<DnsName> delegationPoints,
        Dictionary<DnsName, List<DnsResourceRecord>> generatedDs,
        IReadOnlyList<DnssecKeyEntity> signers,
        uint inception,
        uint expiration,
        uint ttl,
        Dictionary<
            (DnsName Name, DnsResourceRecordTypeEnum Type),
            IReadOnlyList<DnsResourceRecord>
        > rrsigs
    )
    {
        const byte hashAlgorithm = 1; // SHA-1, the only value RFC 5155 assigns.
        byte[] salt = string.IsNullOrEmpty(zone.Nsec3SaltHex)
            ? []
            : Convert.FromHexString(zone.Nsec3SaltHex);

        // RFC 5155 §6/§7.1: Opt-Out is a whole-zone signing decision, not a per-record one -- once
        // a zone actually contains at least one insecure (non-DS) delegation, §7.1 step 2's own
        // construction algorithm sets the Opt-Out bit on *every* NSEC3 RR the zone generates
        // ("If Opt-Out is being used, set the Opt-Out bit to one"), not only the ones adjacent to
        // an unsigned delegation -- this is what lets a delegation's insecure/secure status change
        // later without having to recompute or re-sign the rest of the NSEC3 chain. A delegation
        // point counts as insecure here exactly when this signing pass generated no DS record for
        // it (see ComputeDelegationDsRecordsAsync): either it is a real, non-DS-protected
        // delegation to a zone this server does not itself sign, or it delegates to a local child
        // zone that is not itself DNSSEC-signed -- in both cases, this zone's own signing pass has
        // no DS to vouch for it, which is precisely the condition Opt-Out exists to accommodate. A
        // zone with no delegations at all, or whose every delegation is already DS-covered, needs
        // no Opt-Out and keeps every NSEC3 RR's flag clear, per §3.1.2.1's own "Opt-Out flag clear
        // means this record covers zero unsigned delegations" definition.
        bool useOptOut = delegationPoints.Any(delegationPoint =>
            !generatedDs.ContainsKey(delegationPoint)
        );

        List<(DnsName Original, byte[] Hash)> hashed =
        [
            .. signableNames.Select(name =>
                (name, Nsec3Hasher.ComputeHash(hashAlgorithm, name, zone.Nsec3Iterations, salt))
            ),
        ];
        List<(DnsName Original, byte[] Hash)> ordered =
        [
            .. hashed.OrderBy(pair => pair.Hash, ByteSequenceComparer.Instance),
        ];
        List<DnsResourceRecord> records = [];

        for (int i = 0; i < ordered.Count; i++)
        {
            (DnsName original, byte[] hash) = ordered[i];
            byte[] nextHash = ordered[(i + 1) % ordered.Count].Hash;
            DnsName hashedOwnerName = Nsec3Hasher.ToOwnerName(hash, apexName);

            // Per RFC 5155 §7.1, an empty non-terminal's NSEC3 record carries an empty type
            // bitmap -- its RRSIG (which the NSEC3 record itself still needs) is computed for
            // the NSEC3 RRset at the *hashed* owner name, entirely independent of whatever RRSIG
            // marker would otherwise appear at the original, unhashed name.
            List<DnsResourceRecordTypeEnum> presentTypes = ComputePresentTypes(
                original,
                recordsByName,
                delegationPoints,
                generatedDs
            );
            List<DnsResourceRecordTypeEnum> types =
                presentTypes.Count > 0 ? [.. presentTypes, DnsResourceRecordTypeEnum.RrSig] : [];

            DnsResourceRecord nsec3Record = new(
                hashedOwnerName,
                DnsResourceRecordTypeEnum.Nsec3,
                DnsClassEnum.Internet,
                ttl,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Nsec3,
                    new Nsec3RecordData(
                        hashAlgorithm,
                        useOptOut,
                        zone.Nsec3Iterations,
                        salt,
                        nextHash,
                        types
                    )
                )
            );
            records.Add(nsec3Record);

            SignRrset(
                apexName,
                hashedOwnerName,
                DnsResourceRecordTypeEnum.Nsec3,
                [nsec3Record],
                signers,
                inception,
                expiration,
                ttl,
                rrsigs
            );
        }

        DnsResourceRecord nsec3ParamRecord = new(
            apexName,
            DnsResourceRecordTypeEnum.Nsec3Param,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Nsec3Param,
                new Nsec3ParamRecordData(hashAlgorithm, zone.Nsec3Iterations, salt)
            )
        );
        SignRrset(
            apexName,
            apexName,
            DnsResourceRecordTypeEnum.Nsec3Param,
            [nsec3ParamRecord],
            signers,
            inception,
            expiration,
            ttl,
            rrsigs
        );

        return (records, nsec3ParamRecord);
    }

    /// <summary>
    ///     This method encodes a stored key's DNSKEY RDATA -- the Zone Key flag always set (every
    ///     key this project generates is a zone key), and the Secure Entry Point flag set if,
    ///     and only if, the key is a <see cref="DnssecKeyRoleEnum.Ksk" />.
    /// </summary>
    private static byte[] EncodeDnsKeyRdata(DnssecKeyEntity key) =>
        DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            new DnsKeyRecordData(
                true,
                key.Role == DnssecKeyRoleEnum.Ksk,
                key.Algorithm,
                key.PublicKeyWireFormat
            )
        );

    /// <summary>
    ///     This class compares two byte sequences by unsigned value, most significant byte first
    ///     -- the order RFC 5155's hash chain sorts by.
    /// </summary>
    private sealed class ByteSequenceComparer : IComparer<byte[]>
    {
        /// <summary>
        ///     This property contains the single shared instance of this stateless comparer.
        /// </summary>
        public static readonly ByteSequenceComparer Instance = new();

        /// <inheritdoc />
        public int Compare(byte[] x, byte[] y)
        {
            int commonLength = Math.Min(x.Length, y.Length);

            for (int i = 0; i < commonLength; i++)
                if (x[i] != y[i])
                    return x[i] - y[i];

            return x.Length - y.Length;
        }
    }
}
