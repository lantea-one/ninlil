// Define our namespace.
namespace Ninlil.Dns.Server.Core.Cookies;

/// <summary>
///     This enumeration lists every outcome <see cref="DnsCookieService.Verify" /> can reach for
///     a single request's own DNS Cookie (RFC 7873) option.
/// </summary>
public enum DnsCookieVerificationResultEnum
{
    /// <summary>The request carried no Cookie option at all (or no EDNS(0) at all).</summary>
    NotPresent,

    /// <summary>
    ///     The request carried a Cookie option whose length matches neither "client cookie
    ///     only" (8 bytes) nor "client cookie plus server cookie" (16-40 bytes), per RFC 7873
    ///     §4.
    /// </summary>
    Malformed,

    /// <summary>
    ///     The request carried only an 8-byte client cookie -- either this client has never
    ///     talked to this server before, or it discarded the server cookie this server
    ///     previously handed it.
    /// </summary>
    ClientOnly,

    /// <summary>
    ///     The request carried a server cookie that this server's own current or immediately
    ///     preceding secret reproduces for this client cookie and source address.
    /// </summary>
    Valid,

    /// <summary>
    ///     The request carried a server cookie that matches neither this server's current nor
    ///     immediately preceding secret -- it was never issued by this server for this client
    ///     and source address, which is exactly the off-path-spoofing signal RFC 7873 exists to
    ///     surface.
    /// </summary>
    Invalid,
}
