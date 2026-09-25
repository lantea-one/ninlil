// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This enumeration lists the four outcomes RFC 4035 §4.3 defines for validating a DNS
///     response.
/// </summary>
public enum DnssecValidationStateEnum
{
    /// <summary>
    ///     The chain of trust from a trust anchor down to the answer (or its authenticated
    ///     denial) verified.
    /// </summary>
    Secure,

    /// <summary>
    ///     A signed proof shows there is no chain of trust for this name: an unsigned delegation,
    ///     proven rather than merely missing.
    /// </summary>
    Insecure,

    /// <summary>
    ///     Validation was attempted but something failed to verify: a signature, a DS/DNSKEY match
    ///     or a denial proof. This indicates tampering or misconfiguration, and the resolver answers
    ///     SERVFAIL unless the client set <c>CD</c>.
    /// </summary>
    Bogus,

    /// <summary>
    ///     Validation could not be completed: a resolution failure while building the chain of
    ///     trust, an exhausted query budget, or a root DNSKEY RRset that matches none of the trust
    ///     anchors.
    /// </summary>
    Indeterminate,
}
