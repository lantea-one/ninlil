// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites;

/// <summary>
///     This class binds to the <c>DnsServer:Rewrites</c> section of the application's
///     configuration and controls <see cref="DnsRewriteEngine" />'s own behavior.
/// </summary>
public sealed class DnsRewriteOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Rewrites";

    /// <summary>
    ///     This property controls whether DNS rewrites are consulted at query time at all. Off
    ///     by default, per architectural invariant #6 -- a rewrite genuinely changes a real
    ///     answer for a name this server may otherwise have no authority over at all, so it is a
    ///     capability-expanding toggle an operator should opt into, the same reasoning already
    ///     applied to <c>DnsFilterOptions.Enable</c>. This is deliberately independent of whether
    ///     any <c>DnsRewriteEntity</c> rows actually exist -- an operator can configure rewrites
    ///     through the REST API at any time regardless of this flag, exactly like
    ///     <c>FilterListsController</c> stays fully usable while <c>DnsFilterOptions.Enable</c>
    ///     is <see langword="false" />; only query-time consultation is gated by it.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the TTL, in seconds, a synthesized rewrite answer is given.
    ///     There is no per-rewrite override in this first pass -- a documented, deliberate scope
    ///     cut, not an oversight.
    /// </summary>
    public uint TimeToLiveSeconds { get; set; } = 300;
}
