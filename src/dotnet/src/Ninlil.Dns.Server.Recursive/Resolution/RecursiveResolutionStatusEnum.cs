// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This enumeration lists the outcomes a single iterative resolution can reach.
/// </summary>
public enum RecursiveResolutionStatusEnum
{
    /// <summary>A real answer was found.</summary>
    Success,

    /// <summary>The queried name does not exist at all.</summary>
    NxDomain,

    /// <summary>The queried name exists, but not with the requested type.</summary>
    NoData,

    /// <summary>
    ///     Resolution could not complete -- every name server tried failed or timed out, or a
    ///     configured bound (referral depth, CNAME chain length, query count, or overall
    ///     deadline) was exceeded. A separately bogus DNSSEC validation outcome is not folded
    ///     into this status; <see cref="RecursiveQueryResolver" /> enforces that itself once
    ///     resolution has already returned a status here.
    /// </summary>
    ServFail,
}
