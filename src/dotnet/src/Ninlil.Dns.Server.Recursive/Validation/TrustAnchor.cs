using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This record carries one trust anchor: the DS-form digest of a DNSKEY the resolver trusts
///     without a parent to vouch for it (a "trusted key" in RFC 4035). <see cref="DnssecValidator" />
///     starts every chain of trust from these.
/// </summary>
/// <param name="KeyTag">The trusted key's key tag.</param>
/// <param name="Algorithm">The trusted key's algorithm.</param>
/// <param name="DigestType">Which digest algorithm produced <see cref="Digest" />.</param>
/// <param name="Digest">The trusted key's digest.</param>
public sealed record TrustAnchor(
    ushort KeyTag,
    DnssecAlgorithmEnum Algorithm,
    byte DigestType,
    byte[] Digest
);

/// <summary>
///     This class holds the built-in root zone trust anchors. They only seed
///     <see cref="FileTrustAnchorStore" /> on first run; after that
///     <see cref="TrustAnchorRolloverService" /> keeps the set current (RFC 5011) while
///     <see cref="RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate" /> is on. The values
///     come from IANA's <c>root-anchors.xml</c>: the 2017 KSK (key tag 20326) and its 2024
///     successor (38696). Both are listed because IANA publishes a successor well before the
///     root switches to it, and a resolver must accept either during the overlap. A wrong digest
///     here would stop validation entirely, so check any change against IANA's file.
/// </summary>
public static class RootTrustAnchors
{
    /// <summary>
    ///     This property contains the built-in root zone trust anchors.
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
    ///     one, or the built-in <see cref="Anchors" /> otherwise.
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
