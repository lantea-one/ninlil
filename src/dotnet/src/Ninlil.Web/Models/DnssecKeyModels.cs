using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to generate a new DNSSEC key for a zone
///     (<c>POST /api/zones/{zoneId}/dnssec-keys</c>). The key pair is always generated on the
///     server; an externally generated private key cannot be imported.
/// </summary>
/// <param name="Role">Which role the key plays (ZSK or KSK).</param>
/// <param name="Algorithm">The key's signing algorithm. Algorithms prohibited for signing (RSAMD5, DSA, RSASHA1, DSA-NSEC3-SHA1, RSASHA1-NSEC3-SHA1, ECC-GOST) are rejected with 400.</param>
/// <param name="KeySizeBits">
///     The key size to generate, in bits. Used only by the RSA algorithms, where 0 (the default)
///     means 2048 and any other value must be a multiple of 64 between 2048 and 4096, else the
///     request is rejected with 400; ignored by every other algorithm.
/// </param>
/// <param name="Comments">Free-form operator notes about the key.</param>
public record DnssecKeyCreateRequest(
    DnssecKeyRoleEnum Role,
    DnssecAlgorithmEnum Algorithm,
    int KeySizeBits = 0,
    string Comments = null
);

/// <summary>
///     This class carries the editable state of a DNSSEC key, replaced in full by
///     <c>PUT /api/zones/{zoneId}/dnssec-keys/{id}</c>. A key's role, algorithm and key material
///     are fixed once generated; changing them means generating a new key and retiring the old
///     one. So the retirement flag and notes are the only writable fields.
/// </summary>
/// <param name="IsRetired">
///     <see langword="true" /> moves the key to the <c>Removed</c> lifecycle state, out of the
///     zone; <see langword="false" /> (the default) moves it to <c>Active</c>, whatever state it
///     was in, which also cancels a rollover in progress.
/// </param>
/// <param name="Comments">Free-form operator notes about the key.</param>
public record DnssecKeyWriteRequest(bool IsRetired = false, string Comments = null);

/// <summary>
///     This class carries a DNSSEC key, as returned by the list, get and create endpoints under
///     <c>/api/zones/{zoneId}/dnssec-keys</c>. It never includes the private key. Unlike a TSIG
///     secret or a cloud provider credential, a DNSSEC private key never needs to leave the
///     server: it is used only for signing inside <c>Server.Authoritative</c>, stays encrypted at
///     rest and is never exposed over REST.
/// </summary>
/// <param name="Id">The key's identifier.</param>
/// <param name="ZoneId">The identifier of the zone the key belongs to.</param>
/// <param name="Role">Which role the key plays (ZSK or KSK).</param>
/// <param name="Algorithm">The key's signing algorithm.</param>
/// <param name="PublicKeyWireFormat">The key's public key material, base64-encoded.</param>
/// <param name="KeyTag">The key's precomputed key tag (RFC 4034 Appendix B).</param>
/// <param name="IsRetired">
///     Whether the key has been withdrawn from the zone's DNSKEY RRset: <see langword="true" />
///     when <see cref="LifecycleState" /> is <c>Removed</c>. It is the field
///     <see cref="DnssecKeyWriteRequest.IsRetired" /> writes.
/// </param>
/// <param name="LifecycleState">The key's RFC 6781 lifecycle state.</param>
/// <param name="PublishedAtUtc">When the key was first published in the zone's DNSKEY RRset.</param>
/// <param name="RetiredAtUtc">
///     When the key last stopped being the zone's signing key for its role, or
///     <see langword="null" /> if it never has.
/// </param>
/// <param name="ParentDsConfirmedAtUtc">
///     When an operator confirmed that a KSK rollover's new DS record is published at an
///     externally hosted parent zone, or <see langword="null" /> if no confirmation is recorded
///     (no rollover needs one, or it is still awaited).
/// </param>
/// <param name="Comments">Free-form operator notes about the key.</param>
/// <param name="CreatedAtUtc">When the key was created.</param>
/// <param name="UpdatedAtUtc">When the key was last updated, or <see langword="null" /> if never.</param>
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
///     active key (<c>POST /api/zones/{zoneId}/dnssec-keys/{id}/roll</c>). The scheme follows from
///     the key's <c>Role</c>: Pre-Publish (RFC 6781 §4.1.1.1) for a ZSK and Double-Signature
///     (RFC 6781 §4.1.2) for a KSK. The other scheme cannot be requested.
/// </summary>
/// <param name="Algorithm">
///     The new key's signing algorithm. <see langword="null" /> (the default) keeps the current
///     key's algorithm, which is rejected with 400 when the current key uses an algorithm
///     prohibited for signing; a different value also changes the algorithm in the same rollover.
/// </param>
/// <param name="KeySizeBits">
///     The new key's size, in bits. Used only by the RSA algorithms, where 0 (the default) means
///     2048 and any other value must be a multiple of 64 between 2048 and 4096, else the request
///     is rejected with 400.
/// </param>
public record DnssecKeyRolloverRequest(DnssecAlgorithmEnum? Algorithm = null, int KeySizeBits = 0);
