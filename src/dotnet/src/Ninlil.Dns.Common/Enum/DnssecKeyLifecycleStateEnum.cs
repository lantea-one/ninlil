using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the real states a single <see cref="Wire.Records.DnsKeyRecordData" />
///     moves through across its own lifetime, per RFC 6781 §4.1.1 (Zone Signing Key rollovers)
///     and §4.1.2 (Key Signing Key rollovers) -- the two rollover choreographies
///     <c>Ninlil.Dns.Server.Authoritative.Dnssec.DnssecKeyRolloverBackgroundService</c> actually
///     automates: the Pre-Publish scheme (RFC 6781 §4.1.1.1) for a
///     <see cref="DnssecKeyRoleEnum.Zsk" />, and the Double-Signature scheme (RFC 6781 §4.1.2,
///     Figure 4) for a <see cref="DnssecKeyRoleEnum.Ksk" />. A key generated the ordinary way
///     (<c>Server.Dnssec.IDnssecKeyManagementService.GenerateKeyAsync</c>, with no rollover in
///     progress) starts life directly in <see cref="Active" />, exactly matching that method's own
///     "already active" contract from before this state model existed.
///     <para>
///         The two roles reuse the same five states, but not identically: a ZSK's outgoing key
///         stops signing the moment its replacement takes over (<see cref="Retired" />, still
///         published, RFC 6781's own "new RRSIGs" stage), since nothing outside this zone depends
///         on which specific ZSK produced a given RRSIG. A KSK's outgoing key keeps signing the
///         DNSKEY RRset the entire time -- Figure 4 shows both KSKs' own RRSIGs present right up
///         until the very last stage -- because a parent's DS record, and any resolver holding it
///         as a trust anchor, must always find a currently-signing DNSKEY it can verify against;
///         a KSK rollover reuses <see cref="DoubleSignature" /> for *both* "the new key, not yet
///         canonical" and "the old key, no longer canonical but still co-signing," distinguished
///         only by which one is currently the sole <see cref="Active" /> KSK of the pair (see
///         <c>DnssecKeyRolloverBackgroundService</c>'s own doc comment for exactly how that
///         distinction is computed, entirely from the surrounding key set rather than a stored
///         flag).
///     </para>
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnssecKeyLifecycleStateEnum>>]
public enum DnssecKeyLifecycleStateEnum
{
    /// <summary>
    ///     The key has just been generated and published in the zone's own DNSKEY RRset, but is
    ///     not yet used to sign anything -- RFC 6781 §4.1.1.1's "new DNSKEY" stage for a ZSK's
    ///     incoming replacement. A key stays here until it has been published long enough
    ///     (the DNSKEY RRset's own TTL, plus a configured propagation delay) that every resolver
    ///     which might depend on it has had a real chance to load it, at which point it is
    ///     promoted to <see cref="Active" />. Not a <see cref="DnssecKeyRoleEnum.Ksk" /> state --
    ///     see this enumeration's own doc comment for why a KSK rollover starts its incoming key
    ///     directly in <see cref="DoubleSignature" /> instead.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Published")]
    Published = 0,

    /// <summary>
    ///     The key is the zone's own canonical, currently-signing key of its role: the sole
    ///     signer of ordinary RRsets for a <see cref="DnssecKeyRoleEnum.Zsk" />, or the one whose
    ///     own key tag a parent zone's DS record actually names for a
    ///     <see cref="DnssecKeyRoleEnum.Ksk" />. Every key this project generates outside an
    ///     active rollover starts, and normally stays, here.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Active")]
    Active = 1,

    /// <summary>
    ///     The key is one of two currently co-signing keys of the same role during a KSK
    ///     rollover's own transitional window (RFC 6781 §4.1.2, Figure 4's "new DNSKEY" and
    ///     "DS change" stages) -- both the incoming key before it becomes canonical and the
    ///     outgoing key after it stops being canonical share this one state, see this
    ///     enumeration's own doc comment for how the two are told apart. Also used, more
    ///     narrowly, for a <see cref="DnssecKeyRoleEnum.Zsk" /> whose own rollover an operator
    ///     chose to run as RFC 6781 §4.1.1.2's Double-Signature scheme instead of the ordinary
    ///     Pre-Publish one this project automates by default -- such a key still signs, exactly
    ///     as a KSK in this state does, but neither this project's own rollover automation nor
    ///     its own live-verified test coverage exercises that path; it exists purely so the
    ///     signing pipeline's own "does this key sign" rule stays a single, uniform check across
    ///     every state and role rather than a role-specific special case.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DoubleSignature")]
    DoubleSignature = 2,

    /// <summary>
    ///     The key remains published in the zone's own DNSKEY RRset -- so a resolver that cached
    ///     an RRSIG this key produced before it stepped down can still fetch a DNSKEY RRset that
    ///     verifies it -- but no longer signs anything new. RFC 6781 §4.1.1.1's "new RRSIGs"
    ///     stage for a ZSK's outgoing key. A key stays here until the zone's own maximum record
    ///     TTL, plus a configured propagation delay, has elapsed since it stopped signing, at
    ///     which point it is safe to withdraw entirely (<see cref="Removed" />). Not a
    ///     <see cref="DnssecKeyRoleEnum.Ksk" /> state under this project's own automated
    ///     Double-Signature rollover -- see this enumeration's own doc comment for why an
    ///     outgoing KSK instead moves directly from <see cref="Active" /> back into
    ///     <see cref="DoubleSignature" />, still signing, rather than stopping here first.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Retired")]
    Retired = 3,

    /// <summary>
    ///     The key has been fully withdrawn from the zone's own DNSKEY RRset -- RFC 6781's own
    ///     "DNSKEY removal" stage -- and is no longer published or used to sign anything. Kept on
    ///     record rather than deleted outright, for the same audit-trail reason
    ///     <c>Ninlil.Dns.Data.Abstractions.Entity.DnssecKeyEntity</c>'s own class-level doc
    ///     comment already gives; a manual REST retirement (<c>DnssecKeyWriteRequest.IsRetired</c>
    ///     set <see langword="true" />) also lands a key directly here, matching that field's own
    ///     pre-existing "taken out of active signing use" contract from before this state model
    ///     existed.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Removed")]
    Removed = 4,
}
