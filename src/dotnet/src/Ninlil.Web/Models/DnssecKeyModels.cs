using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to generate a new DNSSEC key for a zone
///     (<c>POST /api/zones/{zoneId}/dnssec-keys</c>). Ninlil only ever generates a brand-new
///     keypair server-side -- there is no way to import an externally-generated private key.
/// </summary>
/// <param name="Role">Which role the key plays (ZSK or KSK).</param>
/// <param name="Algorithm">The key's signing algorithm.</param>
/// <param name="KeySizeBits">
///     The key size to generate, in bits -- meaningful only for the RSA algorithm family; ignored
///     for every other algorithm.
/// </param>
/// <param name="Comments">Free-form operator notes about the key.</param>
public record DnssecKeyCreateRequest(
    DnssecKeyRoleEnum Role,
    DnssecAlgorithmEnum Algorithm,
    int KeySizeBits = 0,
    string Comments = null
);

/// <summary>
///     This class carries the fields used to fully replace an existing DNSSEC key's own mutable
///     state (<c>PUT /api/zones/{zoneId}/dnssec-keys/{id}</c>). A key's role, algorithm, and key
///     material are permanent once generated -- rolling to a different algorithm or role means
///     generating a brand-new key and retiring the old one, not editing this one in place -- so
///     retirement status and operator notes are the only fields this resource ever exposes as
///     writable.
/// </summary>
/// <param name="IsRetired">Whether the key is retired (taken out of active signing use).</param>
/// <param name="Comments">Free-form operator notes about the key.</param>
public record DnssecKeyWriteRequest(bool IsRetired = false, string Comments = null);

/// <summary>
///     This class carries a DNSSEC key's own metadata, for list and single-item responses. This
///     deliberately never includes the key's own private key material -- unlike a TSIG shared
///     secret or a cloud provider credential, a DNSSEC private key never needs to leave this
///     server; it exists purely for <c>Server.Authoritative</c>'s own internal signing, so it
///     stays encrypted at rest and is never exposed over this REST API at all, at any point in a
///     key's lifecycle.
/// </summary>
/// <param name="Id">The key's own identifier.</param>
/// <param name="ZoneId">The identifier of the zone this key belongs to.</param>
/// <param name="Role">Which role the key plays (ZSK or KSK).</param>
/// <param name="Algorithm">The key's signing algorithm.</param>
/// <param name="PublicKeyWireFormat">The key's public key material, base64-encoded.</param>
/// <param name="KeyTag">The key's precomputed key tag (RFC 4034 Appendix B).</param>
/// <param name="IsRetired">
///     Whether the key has been fully withdrawn from the zone's own DNSKEY RRset -- equivalent to
///     <c>LifecycleState == Removed</c>, kept as its own field for the REST/UI contract that
///     predates <see cref="LifecycleState" />.
/// </param>
/// <param name="LifecycleState">The key's own current RFC 6781 lifecycle state.</param>
/// <param name="PublishedAtUtc">When the key was first published in the zone's own DNSKEY RRset.</param>
/// <param name="RetiredAtUtc">
///     When the key most recently stopped being its zone's own canonical, currently-signing key
///     of its role, or <see langword="null" /> if it never has.
/// </param>
/// <param name="ParentDsConfirmedAtUtc">
///     When an operator confirmed a KSK rollover's own new DS record has actually been published
///     at a real, externally-hosted parent zone, or <see langword="null" /> if no such
///     confirmation has been recorded (either because no rollover needs one, or because it is
///     still awaited).
/// </param>
/// <param name="Comments">Free-form operator notes about the key.</param>
/// <param name="CreatedAtUtc">When the key was created.</param>
/// <param name="UpdatedAtUtc">When the key's own metadata was last updated, if ever.</param>
/// <param name="DnsKeyRecord">The key's DNSKEY record in presentation format, as published in the zone.</param>
/// <param name="DsRecord">
///     The SHA-256 DS record a parent zone publishes for this key, in presentation format, or
///     <see langword="null" /> for a ZSK, which the parent never references.
/// </param>
/// <param name="DsDigest">
///     The digest part of <paramref name="DsRecord" /> on its own, as uppercase hexadecimal, for
///     registrars that ask for the key tag, algorithm, digest type and digest separately.
/// </param>
public record DnssecKeyDetail(
    Guid Id,
    Guid ZoneId,
    DnssecKeyRoleEnum Role,
    DnssecAlgorithmEnum Algorithm,
    string PublicKeyWireFormat,
    ushort KeyTag,
    bool IsRetired,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DnssecKeyLifecycleStateEnum LifecycleState,
    DateTimeOffset PublishedAtUtc,
    DateTimeOffset? RetiredAtUtc,
    DateTimeOffset? ParentDsConfirmedAtUtc,
    string DnsKeyRecord,
    string DsRecord,
    string DsDigest
);

/// <summary>
///     This class carries the fields used to start an automated RFC 6781 key rollover for an
///     existing, currently-active key (<c>POST /api/zones/{zoneId}/dnssec-keys/{id}/roll</c>).
///     Ninlil chooses the rollover scheme by the existing key's own <c>Role</c> -- Pre-Publish
///     (RFC 6781 §4.1.1.1) for a ZSK, Double-Signature (RFC 6781 §4.1.2) for a KSK -- there is no
///     way to request the other scheme for a given role through this action.
/// </summary>
/// <param name="Algorithm">
///     The new, incoming key's own signing algorithm -- <see langword="null" /> (the default)
///     reuses the existing key's own algorithm, the ordinary case; a different value here also
///     performs an algorithm rollover in the same pass.
/// </param>
/// <param name="KeySizeBits">
///     The new key's own key size, in bits -- meaningful only for the RSA algorithm family.
/// </param>
public record DnssecKeyRolloverRequest(DnssecAlgorithmEnum? Algorithm = null, int KeySizeBits = 0);
