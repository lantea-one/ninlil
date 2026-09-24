using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog;

/// <summary>
///     This class binds to the <c>DnsServer:QueryLogging</c> section of the application's
///     configuration and controls both <see cref="DnsQueryLogSubscriber" />'s own persistence
///     behavior and <see cref="DnsQueryLogCleanupService" />'s own retention sweep.
///     <para>
///         <b><see cref="PrivacyLevel" /> is this class's one field read live, via
///         <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, with no restart
///         required</b> -- <see cref="DnsQueryLogSubscriber" /> re-reads it fresh for every query
///         it persists. Every other field on this class (<see cref="Enable" />,
///         <see cref="RetentionPeriod" />, <see cref="MaxRetainedEntries" />,
///         <see cref="CleanupInterval" />) keeps this project's older, restart-required
///         <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />-snapshotted posture --
///         <c>Controllers.QueryLoggingSettingsController</c> reports which bucket a given
///         <c>PATCH</c> touched, the same "mixed reload" shape
///         <c>Controllers.RecursiveSettingsController</c> already establishes for a single
///         options class with fields in both categories.
///     </para>
/// </summary>
public sealed class QueryLoggingOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:QueryLogging";

    /// <summary>
    ///     This property controls whether an already-answered query is ever persisted to the
    ///     query log at all. On by default -- a deliberate exception to this project's own usual
    ///     "capability-expanding toggle defaults off" posture, prioritizing out-of-the-box parity
    ///     with how every comparable DNS server (Pi-hole, AdGuard Home) already behaves by
    ///     default. Turning this off stops new entries from being written, but never removes an
    ///     entry already on disk -- <see cref="DnsQueryLogCleanupService" /> still runs regardless
    ///     of this flag, so any data already logged before this was disabled still ages out on its
    ///     own normal schedule instead of sitting there forever.
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    ///     This property contains how long a query log entry is kept before
    ///     <see cref="DnsQueryLogCleanupService" /> removes it, regardless of how many entries
    ///     currently exist. A zero or negative value disables age-based retention entirely,
    ///     leaving <see cref="MaxRetainedEntries" /> as the only bound on the log's own growth.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains the largest number of entries the query log is ever allowed to
    ///     hold, regardless of how recent they are -- a safety cap independent of
    ///     <see cref="RetentionPeriod" />, since a server answering an unusually high query volume
    ///     could otherwise still grow the log unboundedly within a single retention window. A
    ///     value of zero or less disables this cap entirely, leaving <see cref="RetentionPeriod" />
    ///     as the only bound.
    /// </summary>
    public int MaxRetainedEntries { get; set; } = 1_000_000;

    /// <summary>
    ///     This property contains how often <see cref="DnsQueryLogCleanupService" /> sweeps the
    ///     log for entries to remove.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     This property contains how much detail a newly-answered query is actually allowed to
    ///     carry into storage -- applied by <see cref="DnsQueryLogSubscriber" /> at the moment an
    ///     entry is written, never afterward. Defaults to <see cref="QueryLogPrivacyLevelEnum.Full" />,
    ///     today's original, unchanged behavior: this is a new, additive privacy option an
    ///     operator opts into, not a retroactive reduction of a feature that already ships
    ///     <see cref="Enable" />d by default -- turning this dial up only ever happens because an
    ///     operator explicitly chose to.
    /// </summary>
    public QueryLogPrivacyLevelEnum PrivacyLevel { get; set; } = QueryLogPrivacyLevelEnum.Full;
}
