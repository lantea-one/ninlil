// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class carries a single DNSCrypt certificate's fields, per the protocol's own
///     <c>&lt;cert&gt;</c> structure -- what a resolver publishes (via a plain, unencrypted TXT
///     query) so a client can discover its current public key, which cipher to use, and which
///     <c>&lt;client-magic&gt;</c> to tag its own encrypted queries with.
///     <para>
///         Like other models in this project carrying raw <c>byte[]</c> fields, this is a
///         record with a broken, reference-based <c>Equals</c> for those fields -- harmless
///         here since nothing compares two instances directly.
///     </para>
/// </summary>
/// <param name="EsVersion">Which cipher this certificate's own key is for.</param>
/// <param name="ResolverPublicKey">The resolver's own X25519 public key for this certificate, 32 bytes.</param>
/// <param name="ClientMagic">
///     An 8-byte tag a client prefixes every encrypted query built against this certificate
///     with, letting the resolver recognize which certificate (and so, which key) to decrypt
///     that query with.
/// </param>
/// <param name="Serial">
///     This certificate's own version number -- among multiple currently valid certificates,
///     a client should prefer the highest serial.
/// </param>
/// <param name="ValidFrom">The point this certificate becomes valid.</param>
/// <param name="ValidUntil">The point this certificate stops being valid.</param>
/// <param name="Signature">
///     An Ed25519 signature, by the resolver's own long-term provider key, over every field
///     above -- <see langword="null" /> or empty until <see cref="DnsCryptCertificateCodec.BuildSignedData" />'s
///     output has actually been signed.
/// </param>
public sealed record DnsCryptCertificate(
    DnsCryptEsVersion EsVersion,
    byte[] ResolverPublicKey,
    byte[] ClientMagic,
    uint Serial,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    byte[] Signature
);
