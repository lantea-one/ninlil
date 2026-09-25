using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This record carries one trust anchor candidate with its RFC 5011 state, as
///     <see cref="ITrustAnchorStore" /> persists it, so a pending key's hold-down clock survives a
///     restart. <see cref="TrustAnchor" /> is the smaller, state-free shape validation uses.
///     <para>
///         Like <see cref="TrustAnchor" />, this record has a <c>byte[]</c> member, so the
///         generated <c>Equals</c> compares <see cref="Digest" /> by reference. Nothing compares
///         two instances; digests are compared with
///         <see cref="System.MemoryExtensions.SequenceEqual{T}(System.ReadOnlySpan{T},System.ReadOnlySpan{T})" />.
///     </para>
/// </summary>
/// <param name="KeyTag">The key's tag, computed with the REVOKE bit cleared.</param>
/// <param name="Algorithm">The key's algorithm.</param>
/// <param name="DigestType">Which digest algorithm produced <see cref="Digest" />.</param>
/// <param name="Digest">The key's digest, computed with the REVOKE bit cleared.</param>
/// <param name="State">This candidate's current state.</param>
/// <param name="FirstObservedUtc">
///     When this key was first seen as a candidate, which starts its add hold-down clock
///     (RFC 5011). For a seeded anchor it is the seeding time. After promotion to
///     <see cref="TrustAnchorStateEnum.Valid" /> it is kept only for diagnostics.
/// </param>
/// <param name="ValidSinceUtc">
///     When this key was promoted to <see cref="TrustAnchorStateEnum.Valid" />, or
///     <see langword="null" /> if it never was: still <see cref="TrustAnchorStateEnum.AddPend" />,
///     or seeded as already trusted.
/// </param>
/// <param name="MissingSinceUtc">
///     When a <see cref="TrustAnchorStateEnum.Valid" /> key was first found absent, which starts
///     its remove hold-down clock. It is set only while <see cref="State" /> is
///     <see cref="TrustAnchorStateEnum.Missing" />, and cleared when the key reappears or becomes
///     <see cref="TrustAnchorStateEnum.Removed" />.
/// </param>
public sealed record PersistedTrustAnchor(
    ushort KeyTag,
    DnssecAlgorithmEnum Algorithm,
    byte DigestType,
    byte[] Digest,
    TrustAnchorStateEnum State,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset? ValidSinceUtc,
    DateTimeOffset? MissingSinceUtc = null
);
