// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This record pairs a signed <see cref="DnsCryptCertificate" /> with the resolver secret
///     key that matches its public key. The published certificate never contains the secret
///     key, but the resolver needs it to decrypt queries sent for that certificate.
/// </summary>
/// <param name="Certificate">The signed certificate, as published to clients.</param>
/// <param name="ResolverSecretKey">The resolver's X25519 secret key for this certificate.</param>
public sealed record DnsCryptCertificateEntry(
    DnsCryptCertificate Certificate,
    byte[] ResolverSecretKey
);
