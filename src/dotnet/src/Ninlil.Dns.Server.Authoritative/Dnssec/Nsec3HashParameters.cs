// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This record holds the hash parameters of one NSEC3 chain (RFC 5155 §3.1): the hash
///     algorithm, the extra iterations and the salt. <see cref="SignedZoneData" /> reads them from
///     the chain's NSEC3PARAM or NSEC3 records, and <see cref="DenialOfExistenceProofBuilder" />
///     hashes names with them to find the records of a proof.
/// </summary>
/// <param name="HashAlgorithm">The NSEC3 hash algorithm number (1 is SHA-1).</param>
/// <param name="Iterations">The number of additional hash iterations.</param>
/// <param name="Salt">The salt; empty for none.</param>
public sealed record Nsec3HashParameters(byte HashAlgorithm, ushort Iterations, byte[] Salt);
