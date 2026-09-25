using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog;

/// <summary>
///     This class binds to the <c>DnsServer:QueryLogging</c> section of the application's
///     configuration and controls what <see cref="DnsQueryLogSubscriber" /> writes and how
///     <see cref="DnsQueryLogCleanupService" /> prunes.
///     <para>
///         <b><see cref="PrivacyLevel" /> takes effect without a restart</b>:
///         <see cref="DnsQueryLogSubscriber" /> reads it through
///         <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> for every query.
///         The retention fields are read through
///         <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" /> and need a restart, and
///         so does turning <see cref="Enable" /> on (see <see cref="Enable" />).
///         <c>QueryLoggingSettingsController</c> tells the caller whether a <c>PATCH</c> needs a
///         restart.
///     </para>
/// </summary>
public sealed class QueryLoggingOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:QueryLogging";

    /// <summary>
    ///     This property controls whether answered queries are written to the query log. It is on
    ///     by default, an exception to the usual rule that features which expand what the server
    ///     does start off, to match what Pi-hole and AdGuard Home do. Turning it off stops new
    ///     writes at once but keeps existing entries, which <see cref="DnsQueryLogCleanupService" />
    ///     still ages out. Turning it on needs a restart when it was off at startup, since the
    ///     subscriber is registered only if it is on then.
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    ///     This property contains how long a query log entry is kept before
    ///     <see cref="DnsQueryLogCleanupService" /> removes it. A zero or negative value turns the
    ///     age bound off, leaving <see cref="MaxRetainedEntries" /> as the only bound.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains the most entries the query log keeps after a sweep, however
    ///     recent. It is a cap separate from <see cref="RetentionPeriod" />, since a busy server
    ///     could otherwise write a very large log within one retention period. Zero or less turns
    ///     the cap off, leaving <see cref="RetentionPeriod" /> as the only bound.
    /// </summary>
    public int MaxRetainedEntries { get; set; } = 1_000_000;

    /// <summary>
    ///     This property contains how often <see cref="DnsQueryLogCleanupService" /> sweeps the
    ///     log for entries to remove.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     This property contains how much detail about each query is stored.
    ///     <see cref="DnsQueryLogSubscriber" /> applies it when it writes an entry, never later.
    ///     The default is <see cref="QueryLogPrivacyLevelEnum.Full" />; an operator raises it to
    ///     store less.
    /// </summary>
    public QueryLogPrivacyLevelEnum PrivacyLevel { get; set; } = QueryLogPrivacyLevelEnum.Full;
}
