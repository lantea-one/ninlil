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
    ///     Validation was attempted and something failed: a signature, a DS/DNSKEY match, a
    ///     denial proof, or data the chain of trust needs could not be obtained (RFC 4035 §4.3
    ///     counts missing data that DNSSEC says must exist as bogus). Under a configured trust
    ///     anchor a failed DS or DNSKEY lookup or an exhausted query budget is therefore bogus too.
    ///     The resolver answers SERVFAIL unless the client set <c>CD</c>.
    /// </summary>
    Bogus,

    /// <summary>
    ///     No trust anchor is usable, so there is nothing to validate against, or the resolution
    ///     produced no answer to check. The answer is served as it came, without the AD bit. The
    ///     state never means that a DS or DNSKEY lookup failed: that is
    ///     <see cref="Bogus" />.
    /// </summary>
    Indeterminate,
}
