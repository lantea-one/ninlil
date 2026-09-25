// Define our namespace.
namespace Ninlil.Dns.Server.Core.Cookies;

/// <summary>
///     This enumeration lists the outcomes of <see cref="DnsCookieService.Verify" /> for a
///     request's DNS Cookie (RFC 7873) option.
/// </summary>
public enum DnsCookieVerificationResultEnum
{
    /// <summary>The request carried no Cookie option (or no EDNS(0) at all).</summary>
    NotPresent,

    /// <summary>
    ///     The request carried a Cookie option whose length matches neither "client cookie
    ///     only" (8 bytes) nor "client cookie plus server cookie" (16-40 bytes), per RFC 7873
    ///     §4.
    /// </summary>
    Malformed,

    /// <summary>
    ///     The request carried only an 8-byte client cookie: the client has no server cookie from
    ///     this server yet, or discarded it.
    /// </summary>
    ClientOnly,

    /// <summary>
    ///     The request carried a server cookie that the current or previous secret reproduces for
    ///     this client cookie and source address.
    /// </summary>
    Valid,

    /// <summary>
    ///     The request carried a server cookie that neither secret reproduces: it was not issued
    ///     by this server for this client and address. It may be spoofed, older than two
    ///     rotations, or from before a restart.
    /// </summary>
    Invalid,
}
