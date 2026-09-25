using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the states a <see cref="Wire.Records.DnsKeyRecordData" /> key moves
///     through during its life, following the rollover schemes of RFC 6781 that
///     <c>Ninlil.Dns.Server.Authoritative.Dnssec.DnssecKeyRolloverBackgroundService</c> automates:
///     Pre-Publish (§4.1.1.1) for a <see cref="DnssecKeyRoleEnum.Zsk" /> and Double-Signature
///     (§4.1.2, Figure 4) for a <see cref="DnssecKeyRoleEnum.Ksk" />. A key generated outside a
///     rollover (<c>Server.Dnssec.IDnssecKeyManagementService.GenerateKeyAsync</c>) starts in
///     <see cref="Active" />.
///     <para>
///         The two roles use the states differently. An outgoing ZSK stops signing as soon as its
///         replacement takes over (<see cref="Retired" />, still published), because nothing
///         outside the zone depends on which ZSK made an RRSIG. An outgoing KSK keeps signing the
///         DNSKEY RRset until the last stage (Figure 4), because the parent's DS record and any
///         resolver using it as a trust anchor must always find a signing DNSKEY they can verify.
///         A KSK rollover therefore uses <see cref="DoubleSignature" /> for both the incoming and
///         the outgoing key. They are told apart by which one is the <see cref="Active" /> KSK of
///         the pair, worked out from the key set rather than a stored flag (see
///         <c>DnssecKeyRolloverBackgroundService</c>).
///     </para>
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnssecKeyLifecycleStateEnum>>]
public enum DnssecKeyLifecycleStateEnum
{
    /// <summary>
    ///     The key is published in the zone's DNSKEY RRset but does not sign anything yet: the
    ///     "new DNSKEY" stage of RFC 6781 §4.1.1.1 for an incoming ZSK. It moves to
    ///     <see cref="Active" /> once it has been published for the DNSKEY TTL plus a configured
    ///     propagation delay, so resolvers have had time to load it. KSKs do not use this state;
    ///     an incoming KSK starts in <see cref="DoubleSignature" />.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Published")]
    Published = 0,

    /// <summary>
    ///     The key is the zone's current signing key for its role: the signer of ordinary RRsets
    ///     for a <see cref="DnssecKeyRoleEnum.Zsk" />, or the key whose tag the parent's DS record
    ///     names for a <see cref="DnssecKeyRoleEnum.Ksk" />. A key generated outside a rollover
    ///     starts here and normally stays here.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Active")]
    Active = 1,

    /// <summary>
    ///     The key is one of two keys of the same role that both sign during a KSK rollover (the
    ///     "new DNSKEY" and "DS change" stages of RFC 6781 §4.1.2, Figure 4). The incoming key before
    ///     it takes over and the outgoing key after it steps down share this state; the enumeration
    ///     summary explains how they are told apart. A <see cref="DnssecKeyRoleEnum.Zsk" /> can also
    ///     be in this state if an operator runs its rollover as Double-Signature (RFC 6781
    ///     §4.1.1.2). Such a key signs like a KSK here, but the rollover automation never takes that
    ///     path; the state only keeps the "does this key sign" rule the same for every role.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DoubleSignature")]
    DoubleSignature = 2,

    /// <summary>
    ///     The key stays in the zone's DNSKEY RRset but signs nothing new, so a resolver holding an
    ///     RRSIG it made earlier can still verify it. This is the "new RRSIGs" stage of RFC 6781
    ///     §4.1.1.1 for an outgoing ZSK. After the zone's maximum record TTL plus a configured
    ///     propagation delay it is withdrawn (<see cref="Removed" />). The automated KSK rollover
    ///     does not use this state: an outgoing KSK goes from <see cref="Active" /> back to
    ///     <see cref="DoubleSignature" /> and keeps signing.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Retired")]
    Retired = 3,

    /// <summary>
    ///     The key has been withdrawn from the DNSKEY RRset (the "DNSKEY removal" stage of RFC 6781)
    ///     and is neither published nor used to sign. The row is kept rather than deleted, for the
    ///     audit reason given on <c>Ninlil.Dns.Data.Abstractions.Entity.DnssecKeyEntity</c>. Setting
    ///     <c>DnssecKeyWriteRequest.IsRetired</c> to <see langword="true" /> through the REST API also
    ///     moves a key straight here.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Removed")]
    Removed = 4,
}
