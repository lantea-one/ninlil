// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This enumeration lists the results of a denial-of-existence check in
///     <see cref="DenialOfExistenceValidator" />. <see cref="DnssecValidator" /> maps them to
///     validation states: proven is Secure, proven insecure is Insecure and not proven is Bogus.
/// </summary>
public enum DenialOfExistenceOutcomeEnum
{
    /// <summary>
    ///     The records do not prove the claimed denial. A signed negative answer with such a proof
    ///     is Bogus.
    /// </summary>
    NotProven,

    /// <summary>
    ///     The records prove the denial in a signed zone.
    /// </summary>
    Proven,

    /// <summary>
    ///     The records show the name is outside what the zone secures: an NSEC3 Opt-Out span covers
    ///     it (RFC 5155 §6), or the NSEC3 iteration count is above
    ///     <see cref="DenialOfExistenceValidator.MaxNsec3Iterations" /> (RFC 9276 §3.2). The answer
    ///     is served without the AD bit.
    /// </summary>
    ProvenInsecure,
}
