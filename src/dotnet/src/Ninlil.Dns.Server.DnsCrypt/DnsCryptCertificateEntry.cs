// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class pairs a signed <see cref="DnsCryptCertificate" /> with the resolver's own
///     secret key that certificate's public key is half of -- the private counterpart the
///     signed certificate itself never carries, but a resolver needs to actually decrypt a
///     client's query built against it.
/// </summary>
/// <param name="Certificate">The signed certificate, as published to clients.</param>
/// <param name="ResolverSecretKey">This certificate's own resolver X25519 secret key.</param>
public sealed record DnsCryptCertificateEntry(
    DnsCryptCertificate Certificate,
    byte[] ResolverSecretKey
);
