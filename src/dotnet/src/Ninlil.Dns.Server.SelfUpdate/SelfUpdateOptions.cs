// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class binds to the <c>DnsServer:SelfUpdate</c> section of the application's
///     configuration and controls whether, how often, and against what feed
///     <see cref="SelfUpdateCheckService" /> checks whether a newer release of this server
///     exists.
///     <para>
///         <b>Check-only, deliberately, forever.</b> Automatically checking for a new release is
///         the same "self-maintaining capability defaults on" behavior
///         <c>RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate</c> (a sibling settings
///         class this project's own recursive resolver already uses, out of this project's own
///         reach at compile time)
///         already establishes for this project's own homelab deployment target (a box set up
///         once and left running for years, with nobody around to remember to check manually) --
///         but automatically *applying* an update to a running DNS server with no human oversight
///         is a genuine reliability risk, not merely an abuse-vector question: a bad release could
///         break resolution for every client on the network with nobody watching to notice or roll
///         it back. So this feature only ever answers "is a newer version available," surfaced as
///         a real, visible notice (a REST field and an Angular dashboard notification, not a log
///         line an unattended operator will never read) -- actually applying an update is always a
///         distinct, explicit action an operator takes themselves, deliberately outside this
///         class's own scope. See <c>Controllers.SelfUpdateController</c>'s own doc comment for
///         exactly what "applying" means today (an honest instruction, not a fake button).
///     </para>
///     <para>
///         <b>No hardcoded release-feed URL, and a genuine reason why not.</b> This project has no
///         GitHub remote configured today, so hardcoding "check GitHub's own releases API" would point this feature at a project
///         that doesn't exist there -- a false assumption baked into working code, worse than no
///         feature at all. Instead, <see cref="ReleaseFeedUrl" /> is a plain, empty-by-default
///         configuration value: checking itself is still enabled by default (see
///         <see cref="EnableAutomaticCheck" />), it simply has nothing to check against until an
///         operator points it at a real feed they control, once this project actually has a real
///         release process and a real place to publish one. The feed's own expected shape is
///         deliberately minimal and self-describing rather than tied to any one hosting platform's
///         own API -- see <see cref="SelfUpdateReleaseFeedDocument" />'s own doc comment for its
///         exact JSON shape -- so an operator can serve it from anything: a static JSON file next
///         to their own build output, a tiny script, or a real release-hosting platform's API
///         translated into this shape by a small adapter, once one exists.
///     </para>
/// </summary>
public sealed class SelfUpdateOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:SelfUpdate";

    /// <summary>
    ///     This property controls whether <see cref="SelfUpdateCheckService" /> periodically
    ///     checks <see cref="ReleaseFeedUrl" /> for a newer release at all. On by default -- the
    ///     same "pure availability/correctness improvement, no new attack surface" reasoning
    ///     <c>RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate</c> and
    ///     <c>RecursiveServerOptions.EnableServeStale</c> already document for
    ///     themselves: checking for an update, on its own, changes nothing about this server's own
    ///     behavior or exposed surface -- it only ever produces a notice. An empty
    ///     <see cref="ReleaseFeedUrl" /> makes every check a safe, immediate, honest no-op (see
    ///     that property's own doc comment) rather than something an operator must remember to
    ///     turn on later.
    /// </summary>
    public bool EnableAutomaticCheck { get; set; } = true;

    /// <summary>
    ///     This property contains the URL <see cref="SelfUpdateCheckService" /> fetches a
    ///     <see cref="SelfUpdateReleaseFeedDocument" /> JSON document from. Empty by default,
    ///     meaning "nothing to check against yet" -- see this class's own doc comment for why this
    ///     project deliberately does not hardcode an assumed release-hosting location. A check
    ///     against an empty URL is skipped entirely (not attempted and logged as a failure), so
    ///     leaving this unset is a genuinely inert, quiet default rather than a permanently-failing
    ///     background loop.
    /// </summary>
    public string ReleaseFeedUrl { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how often <see cref="SelfUpdateCheckService" /> re-checks
    ///     <see cref="ReleaseFeedUrl" /> for a newer release. Defaults to once every 24 hours --
    ///     frequent enough that an operator sees a real notice within a day of a new release
    ///     going out, infrequent enough to never meaningfully load whatever this feed turns out to
    ///     be hosted on.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(24);
}
