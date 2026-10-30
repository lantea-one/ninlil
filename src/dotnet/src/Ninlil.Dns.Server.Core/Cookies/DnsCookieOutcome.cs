using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Cookies;

/// <summary>
///     This record carries the decision of <see cref="DnsCookieService.Evaluate" /> back to
///     <see cref="Pipeline.DnsQueryProcessingPipeline" />: whether to answer REFUSED or BADCOOKIE
///     instead of dispatching, and which Cookie option, if any, the response carries.
/// </summary>
/// <param name="ShouldRefuse">
///     Whether the query should be answered REFUSED without reaching
///     <see cref="Resolution.IDnsQueryDispatcher" />. Only when
///     <see cref="DnsCookieOptions.Require" /> is set and the request has no Cookie option or a
///     malformed one.
/// </param>
/// <param name="ShouldRespondBadCookie">
///     Whether the query should be answered with <see cref="Common.Enum.DnsResponseCodeEnum.BadCookie" />
///     and no answer data, per RFC 7873 §5.2.3 and §5.2.4. Only when <see cref="DnsCookieOptions.Require" />
///     is set and the request has a client cookie but no valid server cookie.
/// </param>
/// <param name="ResponseOption">
///     The Cookie option the response should carry, or <see langword="null" /> when cookies are
///     disabled, the request had no usable client cookie, or <see cref="ShouldRefuse" /> is
///     set.
/// </param>
public sealed record DnsCookieOutcome(
    bool ShouldRefuse,
    bool ShouldRespondBadCookie,
    DnsEdnsOption ResponseOption
);
