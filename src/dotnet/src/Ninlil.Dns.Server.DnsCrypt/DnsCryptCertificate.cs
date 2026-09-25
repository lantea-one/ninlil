// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This record carries the fields of one DNSCrypt certificate (the protocol's
///     <c>&lt;cert&gt;</c> structure). A resolver publishes its certificates in a plain TXT
///     answer, so a client can learn the current public key, the cipher, and the
///     <c>&lt;client-magic&gt;</c> to put on its encrypted queries.
///     <see cref="DnsCryptCertificateCodec" /> converts it to and from the wire form.
///     <para>
///         Like other records here with <c>byte[]</c> fields, its <c>Equals</c> compares those
///         arrays by reference. Nothing compares two instances, so that does not matter.
///     </para>
/// </summary>
/// <param name="EsVersion">The cipher this certificate's key is for.</param>
/// <param name="ResolverPublicKey">The resolver's X25519 public key for this certificate, 32 bytes.</param>
/// <param name="ClientMagic">
///     An 8-byte tag a client puts in front of every query encrypted for this certificate, so
///     the resolver knows which certificate, and so which key, decrypts it.
/// </param>
/// <param name="Serial">
///     The certificate's version number; among several valid certificates, a client should
///     prefer the highest serial.
/// </param>
/// <param name="ValidFrom">The point this certificate becomes valid.</param>
/// <param name="ValidUntil">The point this certificate stops being valid.</param>
/// <param name="Signature">
///     The Ed25519 signature by the resolver's long-term provider key over the output of
///     <see cref="DnsCryptCertificateCodec.BuildSignedData" />, or empty before signing.
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
