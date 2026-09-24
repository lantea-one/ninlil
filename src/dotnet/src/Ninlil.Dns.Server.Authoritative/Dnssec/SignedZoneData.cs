using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class holds one zone's complete DNSSEC-signed content, computed once by
///     <see cref="ZoneSigningService" /> and served from cache (<see cref="SignedZoneCache" />)
///     thereafter, matching this project's deliberate stance against ever signing live, per query.
/// </summary>
/// <param name="dnsKeyRecords">Every active key's own <see cref="Common.Wire.Records.DnsKeyRecordData" /> record, at the zone apex.</param>
/// <param name="rrsigsByCoveredRrset">
///     Every computed signature, keyed by the owner name and type of the RRset it covers. More
///     than one signature can cover the same RRset when more than one active key of the
///     relevant role exists at once (an overlapping key rollover, most commonly).
/// </param>
/// <param name="denialOfExistenceRecords">
///     The zone's complete NSEC or NSEC3 chain, already in the order a validator's own
///     canonical-order (NSEC) or hash-order (NSEC3) walk expects.
/// </param>
/// <param name="nsec3ParamRecord">
///     The zone's own <see cref="Common.Wire.Records.Nsec3ParamRecordData" /> record, or
///     <see langword="null" /> when the zone uses <see cref="DnssecDenialOfExistenceModeEnum.Nsec" />
///     instead.
/// </param>
/// <param name="generatedDsRecordsByDelegationName">
///     The <see cref="Common.Wire.Records.DsRecordData" /> records this zone generated for its
///     own delegations to other, signed zones this same server also hosts -- the one piece of a
///     child's chain of trust that only its parent, not the child itself, can ever publish.
/// </param>
/// <param name="signatureExpiresAtUtc">
///     The point every signature computed in this pass expires at -- a single, shared value,
///     since every signature <see cref="ZoneSigningService" /> computes in one pass shares one
///     <see cref="Common.Wire.Records.RrSigRecordData.SignatureExpiration" />. Used by
///     <see cref="DnssecResigningBackgroundService" /> to decide when this zone is due for a
///     proactive re-sign, without needing to decode every signature in the zone just to find the
///     earliest one.
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
    ///     This property contains every active key's own DNSKEY record, at the zone apex.
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
    ///     This property contains the zone's own NSEC3PARAM record, or <see langword="null" />
    ///     when the zone uses NSEC instead.
    /// </summary>
    public DnsResourceRecord Nsec3ParamRecord { get; } = nsec3ParamRecord;

    /// <summary>
    ///     This property contains the DS records this zone generated for its own delegations to
    ///     other, signed zones this same server also hosts.
    /// </summary>
    public IReadOnlyDictionary<
        DnsName,
        IReadOnlyList<DnsResourceRecord>
    > GeneratedDsRecordsByDelegationName { get; } = generatedDsRecordsByDelegationName;

    /// <summary>
    ///     This property contains the point every signature in this pass expires at.
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
