// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This enumeration lists the four outcomes RFC 4035 §4.3 defines for validating a DNS
///     response.
/// </summary>
public enum DnssecValidationStateEnum
{
    /// <summary>
    ///     The response's chain of trust, all the way from a configured trust anchor down to the
    ///     answer (or its authenticated denial), verified successfully.
    /// </summary>
    Secure,

    /// <summary>
    ///     A validly signed, authenticated proof established that no chain of trust exists for
    ///     this name at all -- an unsigned delegation, proven absent rather than merely missing.
    /// </summary>
    Insecure,

    /// <summary>
    ///     Enough DNSSEC data existed to attempt validation, but something failed to verify -- a
    ///     signature, a DS/DNSKEY match, or a denial-of-existence proof. This is the state that
    ///     actually indicates tampering or misconfiguration, and the one response a validating
    ///     resolver must never hand a client without being asked to (<c>CD</c>) not to check.
    /// </summary>
    Bogus,

    /// <summary>
    ///     Validation could not be completed at all -- a resolution failure while building the
    ///     chain of trust, an exhausted query budget, or a root DNSKEY RRset that matches none of
    ///     this resolver's own configured trust anchors.
    /// </summary>
    Indeterminate,
}
