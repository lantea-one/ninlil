using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class carries a single trust anchor candidate's full, persisted RFC 5011 state --
///     everything <see cref="ITrustAnchorStore" /> needs to survive a restart without losing a
///     pending key's hold-down clock. <see cref="TrustAnchor" /> is the smaller, state-free
///     shape actual signature verification uses; this is the superset a store round-trips to
///     disk.
///     <para>
///         Like <see cref="TrustAnchor" /> before it, this is a record with a <c>byte[]</c>
///         member, so its compiler-generated <c>Equals</c> compares <see cref="Digest" /> by
///         reference rather than content -- harmless here only because nothing in this project
///         ever compares two instances of this type directly; every actual digest comparison is
///         done explicitly via <see cref="System.MemoryExtensions.SequenceEqual{T}(System.ReadOnlySpan{T},System.ReadOnlySpan{T})" />.
///     </para>
/// </summary>
/// <param name="KeyTag">The key's own key tag.</param>
/// <param name="Algorithm">The key's own algorithm.</param>
/// <param name="DigestType">Which digest algorithm produced <see cref="Digest" />.</param>
/// <param name="Digest">The key's own digest.</param>
/// <param name="State">This candidate's current state.</param>
/// <param name="FirstObservedUtc">
///     The point this key was first observed as a candidate -- the start of its hold-down
///     clock, per RFC 5011 §2.3. Meaningless once <see cref="State" /> is
///     <see cref="TrustAnchorStateEnum.Valid" />; kept only for diagnostics at that point.
/// </param>
/// <param name="ValidSinceUtc">
///     The point this key was promoted to <see cref="TrustAnchorStateEnum.Valid" />, or
///     <see langword="null" /> if it never has been (either still <see cref="TrustAnchorStateEnum.AddPend" />,
///     or seeded directly as an initial, already-trusted anchor -- in which case this stays
///     <see langword="null" /> rather than claiming a promotion that never actually happened).
/// </param>
/// <param name="MissingSinceUtc">
///     The point this key was first observed absent from a check while previously
///     <see cref="TrustAnchorStateEnum.Valid" /> -- the start of its RFC 5011 §4.2 remove
///     hold-down clock. <see langword="null" /> whenever <see cref="State" /> is not
///     <see cref="TrustAnchorStateEnum.Missing" />; cleared the moment the key either reappears
///     (back to <see cref="TrustAnchorStateEnum.Valid" />) or the hold-down elapses (on to
///     <see cref="TrustAnchorStateEnum.Removed" />), so a stale timestamp can never be
///     misread as an active clock once the key has left that state.
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
