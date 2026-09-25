using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class holds one zone's DNSSEC content: DNSKEYs, RRSIGs, the NSEC or NSEC3 chain and
///     generated DS records. <see cref="ZoneSigningService" /> computes it for zones signed here,
///     and <see cref="TransferredSignedZoneDataBuilder" /> rebuilds it from the signed records
///     a secondary zone received by transfer. <see cref="SignedZoneCache" /> holds it and the
///     resolver serves from it.
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
/// <param name="generatedDsRecordsByDelegationName">
///     The <see cref="Common.Wire.Records.DsRecordData" /> records generated for delegations to
///     signed child zones this server also hosts, keyed by delegation point. The parent
///     publishes the DS, not the child.
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
    IReadOnlyDictionary<
        DnsName,
        IReadOnlyList<DnsResourceRecord>
    > generatedDsRecordsByDelegationName,
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
    ///     This property contains the DS records generated for delegations to signed child zones
    ///     this server hosts, keyed by delegation point.
    /// </summary>
    public IReadOnlyDictionary<
        DnsName,
        IReadOnlyList<DnsResourceRecord>
    > GeneratedDsRecordsByDelegationName { get; } = generatedDsRecordsByDelegationName;

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
}
