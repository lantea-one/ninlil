// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This enumeration lists the outcomes of one iterative resolution
///     (<see cref="IterativeResolutionResult.Status" />).
/// </summary>
public enum RecursiveResolutionStatusEnum
{
    /// <summary>An answer was found.</summary>
    Success,

    /// <summary>The queried name does not exist at all.</summary>
    NxDomain,

    /// <summary>The queried name exists, but not with the requested type.</summary>
    NoData,

    /// <summary>
    ///     Resolution could not complete: every name server tried failed or timed out, or a
    ///     configured bound (referral depth, CNAME chain length, query count or overall deadline)
    ///     was exceeded. A Bogus DNSSEC result is not reported here;
    ///     <see cref="RecursiveQueryResolver" /> turns it into SERVFAIL after validation.
    /// </summary>
    ServFail,
}
