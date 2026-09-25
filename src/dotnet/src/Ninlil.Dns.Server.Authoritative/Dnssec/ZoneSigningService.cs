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
///     This class implements <see cref="IZoneSigningService" />. It reads a zone's records and
///     keys from storage and computes the DNSSEC content RFC 4034 and RFC 5155 require: the
///     DNSKEY RRset, an RRSIG over each authoritative RRset, the full NSEC or NSEC3 chain, and DS
///     records for delegations to signed child zones this server also hosts.
///     <para>
///         At a delegation point only the generated DS RRset is signed; NS records and any other
///         stored records there (including a hand-entered DS) are skipped. Names below a
///         delegation point (glue) are signed and included in the chain like any other name.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository this service reads zones from.</param>
/// <param name="recordRepository">The repository this service reads a zone's records from.</param>
/// <param name="keyRepository">The repository this service reads a zone's DNSSEC keys from.</param>
/// <param name="keyManagement">The service this service decrypts a key's private key material through.</param>
/// <param name="options">
///     The signature timing settings, read through <see cref="IOptionsMonitor{TOptions}.CurrentValue" />
///     on every signing call. <see cref="IOptions{TOptions}" /> caches its value for the life of
///     the process, even for a scoped service, and would miss configuration changes.
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
    ///     This constant contains the DS digest type used for generated DS records: 2 (SHA-256),
    ///     which RFC 8624 makes mandatory to implement.
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

        // Every key except Removed goes in the DNSKEY RRset, including keys that do not sign:
        // Published (waiting to take over) and Retired (kept so cached signatures still
        // verify). RFC 6781 rollovers depend on both being published.
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

        // Only Active and DoubleSignature keys sign.
        static bool Signs(DnssecKeyEntity key) =>
            key.LifecycleState
                is DnssecKeyLifecycleStateEnum.Active
                    or DnssecKeyLifecycleStateEnum.DoubleSignature;

        List<DnssecKeyEntity> activeKsks =
        [
            .. activeKeys.Where(key => key.Role == DnssecKeyRoleEnum.Ksk && Signs(key)),
        ];

        // ZSKs sign ordinary RRsets and KSKs sign the DNSKEY RRset (the usual RFC 6781 split).
        // With no signing ZSK, the KSK signs everything; with no signing KSK, the ZSKs sign the
        // DNSKEY RRset.
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

        // A delegation point is any non-apex name with NS records; the apex NS records name
        // this zone's servers.
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

        // The DNSKEY RRset, with the SOA's TTL (3600 if there is no SOA).
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

        // Every ordinary RRset, plus the generated DS RRset at each delegation point.
        foreach ((DnsName name, List<RecordEntity> recordsAtName) in recordsByName)
        {
            bool isDelegation = delegationPoints.Contains(name);

            foreach (
                DnsResourceRecordTypeEnum type in recordsAtName
                    .Select(record => record.Type)
                    .Distinct()
            )
            {
                // NS at a delegation point is the child's data and is not signed by the parent
                // (RFC 4035 §2.2). Every other stored type there is skipped too, including a
                // stored DS record; only the generated DS below is signed.
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
    ///     This method generates DS records for each delegation whose child zone this server hosts
    ///     and signs. The parent must publish the DS, since it is how a validator finds the child's
    ///     key from outside the child. Delegations to other zones get no DS.
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

            // One DS per Active KSK of the child. In a Double-Signature KSK rollover (RFC 6781
            // §4.1.2) the parent swaps the DS at the "DS change" stage rather than publishing
            // both, so a KSK in DoubleSignature (incoming or outgoing) gets no DS here.
            // DnssecKeyRolloverBackgroundService promotes the incoming KSK to Active, which moves
            // the DS to it.
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
    ///     This method signs one RRset with each signing key and stores the RRSIGs in
    ///     <paramref name="rrsigs" /> under the RRset's (name, type). An empty RRset, or keys whose
    ///     algorithms are not in <see cref="DnssecAlgorithmRegistry" />, produce no signatures.
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
    ///     This method checks whether a name's leftmost label is <c>*</c>, which an RRSIG's Labels
    ///     field must not count (RFC 4034 §3.1.3).
    /// </summary>
    private static bool IsWildcardOwner(DnsName name) =>
        name.Labels.Count > 0 && name.Labels[0] is [(byte)'*'];

    /// <summary>
    ///     This method lists every owner name that needs an NSEC or NSEC3 record: each name with
    ///     records, plus each ancestor between it and the apex (empty non-terminals, which exist
    ///     but have no data and still need a denial record). The apex is included.
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
    ///     This method lists the types for a name's NSEC or NSEC3 type bitmap. At a delegation point
    ///     only NS is listed (RFC 4035 §2.2), plus DS when one was generated.
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
    ///     This method builds the zone's NSEC chain (RFC 4034 §4): one record per owner name in
    ///     canonical order, each naming the next and the last naming the first, each signed.
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

            // RFC 4035 §2.3: every NSEC bitmap lists NSEC and RRSIG.
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
    ///     This method builds the zone's NSEC3 chain (RFC 5155 §7.1): one record per owner name's
    ///     hash, in hash order, each naming the next hash and the last naming the first, plus the
    ///     NSEC3PARAM record. The Opt-Out flag is set on every record when the zone has any
    ///     delegation without a DS (see <c>useOptOut</c>). Insecure delegations stay in the chain.
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

        // RFC 5155 §7.1: Opt-Out applies to the whole zone. If any delegation has no generated
        // DS (a child not hosted and signed here), every NSEC3 record gets the flag. With no
        // insecure delegations the flag stays clear, which per §3.1.2.1 means no record covers
        // an unsigned delegation.
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

            // RFC 5155 §7.1: an empty non-terminal gets an empty bitmap. The NSEC3 record's own
            // RRSIG is at the hashed name and does not affect the bitmap. For other names RRSIG
            // is added, including insecure delegation points.
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
    ///     This method encodes a stored key's DNSKEY RDATA with the Zone Key flag set and the
    ///     Secure Entry Point flag set only for a <see cref="DnssecKeyRoleEnum.Ksk" />.
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
    ///     This class compares byte sequences as unsigned values, most significant byte first, the
    ///     order of the RFC 5155 hash chain.
    /// </summary>
    private sealed class ByteSequenceComparer : IComparer<byte[]>
    {
        /// <summary>
        ///     This field contains the shared instance.
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
