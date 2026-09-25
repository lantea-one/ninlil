using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one DNSSEC signing key of a zone, either a Key-Signing Key or a
///     Zone-Signing Key (RFC 6781). A key generated through
///     <c>Server.Dnssec.IDnssecKeyManagementService.GenerateKeyAsync</c> outside a rollover starts
///     active and stays so until an operator retires it. A key created by an automated rollover
///     starts in <see cref="DnssecKeyLifecycleStateEnum.Published" /> (a
///     <see cref="DnssecKeyRoleEnum.Zsk" />, Pre-Publish, RFC 6781 §4.1.1.1) or
///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" /> (a
///     <see cref="DnssecKeyRoleEnum.Ksk" />, Double-Signature, RFC 6781 §4.1.2), and
///     <c>Server.Authoritative.Dnssec.DnssecKeyRolloverBackgroundService</c> moves it through the
///     remaining states of <see cref="DnssecKeyLifecycleStateEnum" />.
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
    ///     This property contains the public key, encoded as
    ///     <see cref="Common.Wire.Records.DnsKeyRecordData.PublicKey" /> stores it, so it can be
    ///     served directly.
    /// </summary>
    public byte[] PublicKeyWireFormat { get; set; }

    /// <summary>
    ///     This property contains the private key, encrypted at rest. Like
    ///     <see cref="TsigKeyEntity.SecretEncrypted" /> it cannot be a one-way hash, because signing
    ///     needs the key itself.
    /// </summary>
    public string PrivateKeyPkcs8Encrypted { get; set; }

    /// <summary>
    ///     This property contains this key's precomputed key tag (RFC 4034 Appendix B), so
    ///     <see cref="Common.Wire.Records.RrSigRecordData.KeyTag" /> and
    ///     <see cref="Common.Wire.Records.DsRecordData.KeyTag" /> can be filled in without
    ///     recomputing it.
    /// </summary>
    public ushort KeyTag { get; set; }

    /// <summary>
    ///     This property contains this key's lifecycle state.
    ///     <c>Server.Authoritative.Dnssec.ZoneSigningService</c> uses it to decide which keys to
    ///     publish in the DNSKEY RRset (every state except
    ///     <see cref="DnssecKeyLifecycleStateEnum.Removed" />) and which sign
    ///     (<see cref="DnssecKeyLifecycleStateEnum.Active" /> and
    ///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" />). A removed key's row is kept
    ///     rather than deleted, so there is still a record of which key made which published
    ///     signature.
    /// </summary>
    public DnssecKeyLifecycleStateEnum LifecycleState { get; set; } =
        DnssecKeyLifecycleStateEnum.Active;

    /// <summary>
    ///     This property contains when this key was first published in the zone's DNSKEY RRset: the
    ///     same as <see cref="EntityBase.CreatedAtUtc" /> for a key generated outside a rollover, or
    ///     the moment an incoming rollover key entered
    ///     <see cref="DnssecKeyLifecycleStateEnum.Published" /> or
    ///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" />.
    ///     <c>DnssecKeyRolloverBackgroundService</c> waits the DNSKEY TTL plus the propagation delay
    ///     from this time before promoting the key to <see cref="DnssecKeyLifecycleStateEnum.Active" />.
    /// </summary>
    public DateTimeOffset PublishedAtUtc { get; set; }

    /// <summary>
    ///     This property contains when this key last stopped being the zone's current signing key
    ///     of its role, or <see langword="null" /> if it never has. It is set when a
    ///     <see cref="DnssecKeyRoleEnum.Zsk" /> enters <see cref="DnssecKeyLifecycleStateEnum.Retired" />,
    ///     or when a <see cref="DnssecKeyRoleEnum.Ksk" /> goes back to
    ///     <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" /> because its replacement was
    ///     promoted. <c>DnssecKeyRolloverBackgroundService</c> measures the final wait from it (the
    ///     zone's maximum record TTL for a ZSK, the relevant DS TTL for a KSK) before moving the key to
    ///     <see cref="DnssecKeyLifecycleStateEnum.Removed" />.
    /// </summary>
    public DateTimeOffset? RetiredAtUtc { get; set; }

    /// <summary>
    ///     This property contains when an operator confirmed, through
    ///     <c>DnssecKeysController.ConfirmParentDsUpdated</c>, that the DS record for this
    ///     <see cref="DnssecKeyRoleEnum.Ksk" />'s replacement is published at the parent. It only
    ///     matters when the parent zone is not hosted on this server; a locally hosted parent gets
    ///     its DS records automatically (see <c>DnssecKeyRolloverBackgroundService</c>). It is
    ///     <see langword="null" /> until confirmed. Ninlil cannot see an external registrar's DS
    ///     record, so the final removal wait does not start until an operator sets it.
    /// </summary>
    public DateTimeOffset? ParentDsConfirmedAtUtc { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about the key.
    /// </summary>
    public string Comments { get; set; }
}
