// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class binds to the <c>DnsServer:SelfUpdate</c> section of the application's
///     configuration and controls whether, how often, and against what feed
///     <see cref="SelfUpdateCheckService" /> checks whether a newer release of this server
///     exists. <c>SelfUpdateSettingsController</c> edits these settings at runtime.
///     <para>
///         <b>Check only.</b> Checking for a release is on by default, like
///         <c>RecursiveServerOptions.EnableAutomaticTrustAnchorUpdate</c>, because a home or
///         small-office server often runs for years with nobody checking by hand. Applying an
///         update automatically is not offered: a bad release could break resolution for every
///         client with nobody watching to roll it back. The result is shown as a notice in the
///         REST status and the web UI dashboard, and applying an update is a manual step (see
///         <c>Controllers.SelfUpdateController</c>).
///     </para>
///     <para>
///         <b>No built-in feed URL.</b> <see cref="ReleaseFeedUrl" /> is empty by default, so
///         nothing is fetched until an operator sets it, even though
///         <see cref="EnableAutomaticCheck" /> is on. The feed is a small JSON document tied to
///         no hosting platform (see <see cref="SelfUpdateReleaseFeedDocument" />), so it can be
///         a static file, a script's output, or a hosting platform's release API translated by a
///         small adapter.
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
    ///     checks <see cref="ReleaseFeedUrl" /> for a newer release. It is on by default because a
    ///     check changes nothing about how the server behaves; it only produces a notice. With an
    ///     empty <see cref="ReleaseFeedUrl" /> each check records an "unconfigured" status without
    ///     fetching anything. The manual check in <c>SelfUpdateController</c> runs even when this
    ///     is off.
    /// </summary>
    public bool EnableAutomaticCheck { get; set; } = true;

    /// <summary>
    ///     This property contains the URL <see cref="SelfUpdateCheckService" /> fetches a
    ///     <see cref="SelfUpdateReleaseFeedDocument" /> JSON document from. It is empty by
    ///     default, and an empty URL is not fetched: the check records an "unconfigured" status
    ///     instead of logging a failure, so leaving it unset is quiet.
    /// </summary>
    public string ReleaseFeedUrl { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how often <see cref="SelfUpdateCheckService" /> re-checks
    ///     <see cref="ReleaseFeedUrl" /> for a newer release. The default of 24 hours shows a notice
    ///     within about a day of a release without loading the feed's host. The service wakes every
    ///     15 minutes to see whether a check is due, so this is effectively rounded up to the next
    ///     15-minute tick.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(24);
}
