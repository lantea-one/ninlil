using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class holds one zone's DNSSEC content: DNSKEYs, RRSIGs, the NSEC or NSEC3 chain and
///     generated DS records. <see cref="ZoneSigningService" /> computes it for zones signed here,
///     and <see cref="TransferredSignedZoneDataBuilder" /> rebuilds it from the signed records
///     a secondary zone received by transfer. <see cref="SignedZoneCache" /> holds it and the
///     resolver serves from it.
///     <para>
///         The chain describes itself: <see cref="DenialOfExistenceMode" /> and
///         <see cref="Nsec3Parameters" /> come from the NSEC3PARAM or the chain records, never from
///         the local zone row. A secondary's row keeps default settings, while the primary's
///         chain may use any iterations and salt, and proofs must hash with the chain's own values
///         (RFC 5155 §4).
///     </para>
/// </summary>
/// <param name="dnsKeyRecords">The apex DNSKEY records: one per key that is not <c>Removed</c>.</param>
/// <param name="rrsigsByCoveredRrset">
///     Every signature, keyed by the owner name and type of the RRset it covers. An RRset has
///     several when more than one key of the role is signing, as during a rollover.
/// </param>
/// <param name="denialOfExistenceRecords">
///     The zone's complete NSEC chain in canonical order, or NSEC3 chain in hash order.
/// </param>
/// <param name="nsec3ParamRecord">
///     The zone's <see cref="Common.Wire.Records.Nsec3ParamRecordData" /> record, or
///     <see langword="null" /> when the zone uses <see cref="DnssecDenialOfExistenceModeEnum.Nsec" />
///     instead.
/// </param>
/// <param name="dsRecordsByDelegationName">
///     The <see cref="Common.Wire.Records.DsRecordData" /> RRset of each delegation point, keyed
///     by delegation point: for a signed primary, the union of the DS records generated for a
///     signed child this server hosts and the DS records stored at that name, without
///     duplicates; for a secondary, the DS records as transferred. The parent publishes the DS,
///     not the child.
/// </param>
/// <param name="signatureExpiresAtUtc">
///     When the signatures expire. Every signature from one signing pass shares the same
///     <see cref="Common.Wire.Records.RrSigRecordData.SignatureExpiration" />, so one value
///     tells <see cref="DnssecResigningBackgroundService" /> when the zone is due, without
///     decoding every RRSIG.
/// </param>
public sealed class SignedZoneData(
    IReadOnlyList<DnsResourceRecord> dnsKeyRecords,
    IReadOnlyDictionary<
        (DnsName Name, DnsResourceRecordTypeEnum Type),
        IReadOnlyList<DnsResourceRecord>
    > rrsigsByCoveredRrset,
    IReadOnlyList<DnsResourceRecord> denialOfExistenceRecords,
    DnsResourceRecord nsec3ParamRecord,
    IReadOnlyDictionary<DnsName, IReadOnlyList<DnsResourceRecord>> dsRecordsByDelegationName,
    DateTimeOffset signatureExpiresAtUtc
)
{
    /// <summary>
    ///     This property contains the apex DNSKEY records.
    /// </summary>
    public IReadOnlyList<DnsResourceRecord> DnsKeyRecords { get; } = dnsKeyRecords;

    /// <summary>
    ///     This property contains every computed signature, keyed by the owner name and type of
    ///     the RRset it covers.
    /// </summary>
    public IReadOnlyDictionary<
        (DnsName Name, DnsResourceRecordTypeEnum Type),
        IReadOnlyList<DnsResourceRecord>
    > RrsigsByCoveredRrset { get; } = rrsigsByCoveredRrset;

    /// <summary>
    ///     This property contains the zone's complete NSEC or NSEC3 chain.
    /// </summary>
    public IReadOnlyList<DnsResourceRecord> DenialOfExistenceRecords { get; } =
        denialOfExistenceRecords;

    /// <summary>
    ///     This property contains the zone's NSEC3PARAM record, or <see langword="null" />
    ///     when the zone uses NSEC instead.
    /// </summary>
    public DnsResourceRecord Nsec3ParamRecord { get; } = nsec3ParamRecord;

    /// <summary>
    ///     This property tells which kind of chain <see cref="DenialOfExistenceRecords" /> holds:
    ///     NSEC3 when <see cref="Nsec3ParamRecord" /> is set or the first chain record is an
    ///     NSEC3, otherwise NSEC.
    /// </summary>
    public DnssecDenialOfExistenceModeEnum DenialOfExistenceMode { get; } =
        DetermineMode(nsec3ParamRecord, denialOfExistenceRecords);

    /// <summary>
    ///     This property contains the hash parameters of the NSEC3 chain, or
    ///     <see langword="null" /> for an NSEC chain. They come from the NSEC3PARAM record
    ///     (RFC 5155 §4: the parameters an authoritative server hashes with) and, without one,
    ///     from the first NSEC3 record of the chain.
    /// </summary>
    public Nsec3HashParameters Nsec3Parameters { get; } =
        DetermineNsec3Parameters(nsec3ParamRecord, denialOfExistenceRecords);

    /// <summary>
    ///     This property contains the DS RRset of each delegation point (generated and stored
    ///     records without duplicates, or the transferred records on a secondary), keyed by
    ///     delegation point.
    /// </summary>
    public IReadOnlyDictionary<
        DnsName,
        IReadOnlyList<DnsResourceRecord>
    > DsRecordsByDelegationName { get; } = dsRecordsByDelegationName;

    /// <summary>
    ///     This property contains when the signatures from this pass expire.
    /// </summary>
    public DateTimeOffset SignatureExpiresAtUtc { get; } = signatureExpiresAtUtc;

    /// <summary>
    ///     This method looks up the covering signatures for a single RRset, if any exist.
    /// </summary>
    /// <param name="name">The RRset's owner name.</param>
    /// <param name="type">The RRset's type.</param>
    /// <returns>The covering signatures, or an empty list if the RRset was not signed.</returns>
    public IReadOnlyList<DnsResourceRecord> GetSignatures(
        DnsName name,
        DnsResourceRecordTypeEnum type
    ) => RrsigsByCoveredRrset.GetValueOrDefault((name, type), []);

    /// <summary>
    ///     This method decides the chain's mode from the NSEC3PARAM record and the chain.
    /// </summary>
    private static DnssecDenialOfExistenceModeEnum DetermineMode(
        DnsResourceRecord nsec3Param,
        IReadOnlyList<DnsResourceRecord> chain
    ) =>
        nsec3Param is not null
        || (chain.Count > 0 && chain[0].Type == DnsResourceRecordTypeEnum.Nsec3)
            ? DnssecDenialOfExistenceModeEnum.Nsec3
            : DnssecDenialOfExistenceModeEnum.Nsec;

    /// <summary>
    ///     This method reads the NSEC3 hash parameters from the NSEC3PARAM record, else from the
    ///     first NSEC3 record of the chain. It returns <see langword="null" /> for an NSEC chain
    ///     and for NSEC3 records that cannot be decoded.
    /// </summary>
    private static Nsec3HashParameters DetermineNsec3Parameters(
        DnsResourceRecord nsec3Param,
        IReadOnlyList<DnsResourceRecord> chain
    )
    {
        if (nsec3Param is not null)
        {
            return
                DnsRecordDataCodecRegistry.Decode(nsec3Param.Type, nsec3Param.Data)
                    is Nsec3ParamRecordData param
                ? new Nsec3HashParameters(param.HashAlgorithm, param.Iterations, param.Salt)
                : null;
        }

        if (chain.Count > 0 && chain[0].Type == DnsResourceRecordTypeEnum.Nsec3)
        {
            return
                DnsRecordDataCodecRegistry.Decode(chain[0].Type, chain[0].Data)
                    is Nsec3RecordData first
                ? new Nsec3HashParameters(first.HashAlgorithm, first.Iterations, first.Salt)
                : null;
        }

        return null;
    }
}
