using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Cookies;

/// <summary>
///     This record carries <see cref="DnsCookieService.Evaluate" />'s own decision back to
///     <see cref="Pipeline.DnsQueryProcessingPipeline" />: whether the query should be refused
///     or answered with a BADCOOKIE error instead of being dispatched at all, and/or which
///     Cookie option (if any) the eventual response should carry.
/// </summary>
/// <param name="ShouldRefuse">
///     Whether the query should be answered REFUSED without ever reaching
///     <see cref="Resolution.IDnsQueryDispatcher" /> -- reached only when
///     <see cref="DnsCookieOptions.Require" /> is set and the request carried no cookie
///     information to even respond to (no EDNS(0) at all, or a malformed Cookie option with no
///     recoverable client cookie).
/// </param>
/// <param name="ShouldRespondBadCookie">
///     Whether the query should be answered with <see cref="Common.Enum.DnsResponseCodeEnum.BadCookie" />
///     and no answer data, per RFC 7873 §5.2 -- reached only when
///     <see cref="DnsCookieOptions.Require" /> is set and the request carried a client cookie
///     but not yet a server cookie this server itself issued.
/// </param>
/// <param name="ResponseOption">
///     The Cookie option the eventual response should carry, or <see langword="null" /> if none
///     should be attached (the request carried no cookie information at all, or
///     <see cref="ShouldRefuse" /> is set).
/// </param>
public sealed record DnsCookieOutcome(
    bool ShouldRefuse,
    bool ShouldRespondBadCookie,
    DnsEdnsOption ResponseOption
);
