using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single DNSSEC signing key belonging to a zone -- either half of
///     the Key-Signing Key/Zone-Signing Key pair RFC 6781 describes. A key generated directly
///     through <c>Server.Dnssec.IDnssecKeyManagementService.GenerateKeyAsync</c>, with no
///     rollover in progress, is created already active and stays that way until an operator
///     manually retires it. A key created as part of an automated RFC 6781 rollover instead
///     starts life in <see cref="DnssecKeyLifecycleStateEnum.Published" /> (a
///     <see cref="DnssecKeyRoleEnum.Zsk" />, per RFC 6781 §4.1.1.1's Pre-Publish scheme) or
///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" /> (a
///     <see cref="DnssecKeyRoleEnum.Ksk" />, per RFC 6781 §4.1.2's Double-Signature scheme) and
///     is walked the rest of the way by
///     <c>Server.Authoritative.Dnssec.DnssecKeyRolloverBackgroundService</c> -- see
///     <see cref="LifecycleState" />'s own enumeration for the full state model both paths share.
/// </summary>
public class DnssecKeyEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the zone this key belongs to.
    /// </summary>
    public Guid ZoneId { get; set; }

    /// <summary>
    ///     This property contains which role this key plays within its zone.
    /// </summary>
    public DnssecKeyRoleEnum Role { get; set; }

    /// <summary>
    ///     This property contains this key's signing algorithm.
    /// </summary>
    public DnssecAlgorithmEnum Algorithm { get; set; }

    /// <summary>
    ///     This property contains the public key, encoded exactly as
    ///     <see cref="Common.Wire.Records.DnsKeyRecordData.PublicKey" /> stores it -- ready to
    ///     serve directly, with no further decoding needed.
    /// </summary>
    public byte[] PublicKeyWireFormat { get; set; }

    /// <summary>
    ///     This property contains the private key, encrypted at rest. Like
    ///     <see cref="TsigKeyEntity.SecretEncrypted" />, this cannot be a one-way hash: producing
    ///     a signature requires the literal private key material back, not just a means of
    ///     comparing against it.
    /// </summary>
    public string PrivateKeyPkcs8Encrypted { get; set; }

    /// <summary>
    ///     This property contains this key's precomputed key tag (RFC 4034 Appendix B), so every
    ///     <see cref="Common.Wire.Records.RrSigRecordData.KeyTag" />/<see cref="Common.Wire.Records.DsRecordData.KeyTag" />
    ///     that references this key can be filled in without recomputing it from the public key
    ///     bytes each time.
    /// </summary>
    public ushort KeyTag { get; set; }

    /// <summary>
    ///     This property contains this key's current position in its own lifecycle -- the real
    ///     source of truth <c>Server.Authoritative.Dnssec.ZoneSigningService</c> reads to
    ///     decide both which keys to publish in the zone's own DNSKEY RRset at all (every state
    ///     except <see cref="DnssecKeyLifecycleStateEnum.Removed" />) and which of those actually
    ///     sign anything (<see cref="DnssecKeyLifecycleStateEnum.Active" /> or
    ///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" /> only) -- kept on record
    ///     rather than deleted once <see cref="DnssecKeyLifecycleStateEnum.Removed" />, since
    ///     removing a key's own row entirely, rather than simply excluding it from future
    ///     signing, is what actually risks losing the audit trail of which key produced which
    ///     already-published signature.
    /// </summary>
    public DnssecKeyLifecycleStateEnum LifecycleState { get; set; } =
        DnssecKeyLifecycleStateEnum.Active;

    /// <summary>
    ///     This property contains when this key was first published in the zone's own DNSKEY
    ///     RRset -- for a key generated the ordinary way, the same instant as
    ///     <see cref="EntityBase.CreatedAtUtc" />; for a rollover's own incoming key, the
    ///     moment it entered <see cref="DnssecKeyLifecycleStateEnum.Published" /> or
    ///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" />. This is the timestamp
    ///     <c>DnssecKeyRolloverBackgroundService</c> measures the DNSKEY-RRset-TTL-plus-
    ///     propagation-delay pre-roll wait against before promoting the key to
    ///     <see cref="DnssecKeyLifecycleStateEnum.Active" />.
    /// </summary>
    public DateTimeOffset PublishedAtUtc { get; set; }

    /// <summary>
    ///     This property contains when this key most recently stopped being its zone's own
    ///     canonical, currently-signing key of its role -- <see langword="null" /> for a key that
    ///     has never stepped down. Set the moment a <see cref="DnssecKeyRoleEnum.Zsk" /> enters
    ///     <see cref="DnssecKeyLifecycleStateEnum.Retired" />, or a <see cref="DnssecKeyRoleEnum.Ksk" />
    ///     is displaced back into <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" /> by
    ///     its own replacement's promotion -- in both cases, this is the clock
    ///     <c>DnssecKeyRolloverBackgroundService</c> measures the final safe-removal wait
    ///     against (the zone's own maximum record TTL for a ZSK, or the relevant DS TTL for a
    ///     KSK -- see that service's own doc comment) before withdrawing the key entirely
    ///     (<see cref="DnssecKeyLifecycleStateEnum.Removed" />).
    /// </summary>
    public DateTimeOffset? RetiredAtUtc { get; set; }

    /// <summary>
    ///     This property contains when an operator confirmed, via
    ///     <c>Controllers.DnssecKeysController.ConfirmParentDsUpdated</c>, that this
    ///     <see cref="DnssecKeyRoleEnum.Ksk" />'s own replacement DS record has actually been
    ///     published at its zone's real parent -- meaningful only when that parent is not itself
    ///     a zone this same server hosts (see <c>DnssecKeyRolloverBackgroundService</c>'s own
    ///     doc comment for why a locally hosted parent needs no such confirmation at all, and
    ///     generates its own child DS records automatically instead). <see langword="null" />
    ///     until confirmed; this project has no way to observe a real, externally-hosted
    ///     registrar's own DS record on its own, so this key's own final removal wait never
    ///     starts running at all until an operator sets this explicitly.
    /// </summary>
    public DateTimeOffset? ParentDsConfirmedAtUtc { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about the key.
    /// </summary>
    public string Comments { get; set; }
}
