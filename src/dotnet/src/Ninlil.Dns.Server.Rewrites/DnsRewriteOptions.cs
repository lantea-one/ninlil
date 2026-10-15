// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites;

/// <summary>
///     This class binds to the <c>DnsServer:Rewrites</c> configuration section and controls
///     <see cref="DnsRewriteEngine" />. <c>RewriteSettingsController</c> edits it at runtime, and
///     the engine reads it live. The section is cluster-synced: a write on one node, including the
///     switch that enabling Safe Search turns on, is applied to every peer's configuration file,
///     where the engine picks it up at once. An environment variable pins a value on its node.
/// </summary>
public sealed class DnsRewriteOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Rewrites";

    /// <summary>
    ///     This property controls whether rewrites are checked at query time. It is off by default
    ///     because a rewrite changes the answer for a name this server may have no authority over,
    ///     so an operator opts in (as with <c>DnsFilterOptions.Enable</c>). It does not depend on
    ///     whether any <c>DnsRewriteEntity</c> rows exist: rewrites can be managed through the REST
    ///     API while this is off, and only query-time matching is gated.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the TTL, in seconds, of every rewrite answer. There is no
    ///     per-rewrite TTL.
    /// </summary>
    public uint TimeToLiveSeconds { get; set; } = 300;
}
