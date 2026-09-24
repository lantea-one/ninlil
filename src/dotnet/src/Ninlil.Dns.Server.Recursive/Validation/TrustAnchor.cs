using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class carries a single trust anchor -- a DS-form digest of a DNSKEY this resolver
///     trusts with no parent to vouch for it, per RFC 4035 §4.3's definition of a "trusted key."
/// </summary>
/// <param name="KeyTag">The trusted key's own key tag.</param>
/// <param name="Algorithm">The trusted key's own algorithm.</param>
/// <param name="DigestType">Which digest algorithm produced <see cref="Digest" />.</param>
/// <param name="Digest">The trusted key's own digest.</param>
public sealed record TrustAnchor(
    ushort KeyTag,
    DnssecAlgorithmEnum Algorithm,
    byte DigestType,
    byte[] Digest
);

/// <summary>
///     This class holds the hardcoded root zone trust anchor(s) every chain-of-trust validation
///     ultimately has to reach -- this is only the *initial* seed; <see cref="TrustAnchorRolloverService" />
///     keeps the running set current afterward, per RFC 5011, when
///     <see cref="RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate" /> is on. This seed
///     was re-verified directly against IANA's own, live <c>root-anchors.xml</c> (rather than
///     solely relying on a recalled value) before being hardcoded here, since a stale or wrong
///     digest would leave this resolver unable to validate anything at all. Current as of
///     2026-08-29: both the still-valid 2017 KSK and its already-published 2024 successor are
///     listed, since IANA publishes a successor well ahead of the root zone actually switching to
///     signing with it, and a resolver needs to accept either during that overlap.
/// </summary>
public static class RootTrustAnchors
{
    /// <summary>
    ///     This property contains the root zone's own trust anchor(s).
    /// </summary>
    public static IReadOnlyList<TrustAnchor> Anchors { get; } =
    [
        new(
            20326,
            DnssecAlgorithmEnum.RsaSha256,
            2,
            Convert.FromHexString(
                "E06D44B80B8F1D39A95C0B0D7C65D08458E880409BBC683457104237C7F8EC8D"
            )
        ),
        new(
            38696,
            DnssecAlgorithmEnum.RsaSha256,
            2,
            Convert.FromHexString(
                "683D2D0ACB8C9B712A1948B27F741219298D0A450D612C483AF444A4C0FB2B16"
            )
        ),
    ];

    /// <summary>
    ///     This method resolves the effective trust anchor list:
    ///     <see cref="RecursiveServerOptions.TrustAnchorsOverride" /> when the operator configured
    ///     one, or this class's own hardcoded <see cref="Anchors" /> otherwise.
    /// </summary>
    /// <param name="options">The options to read an override from, if any.</param>
    /// <returns>The effective trust anchor list.</returns>
    public static IReadOnlyList<TrustAnchor> Resolve(RecursiveServerOptions options) =>
        options.TrustAnchorsOverride.Count > 0
            ?
            [
                .. options.TrustAnchorsOverride.Select(anchor => new TrustAnchor(
                    anchor.KeyTag,
                    anchor.Algorithm,
                    anchor.DigestType,
                    Convert.FromHexString(anchor.DigestHex)
                )),
            ]
            : Anchors;
}
