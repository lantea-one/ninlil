using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the query logging settings (<c>QueryLoggingOptions</c>)
///     that <c>GET /api/query-logging-settings</c> returns. A <c>PATCH</c> to the same endpoint
///     accepts the same fields.
/// </summary>
/// <param name="Enable">Whether answered queries are written to the query log. Defaults to <see langword="true" />.</param>
/// <param name="RetentionPeriod">How long a query log entry is kept. Defaults to 30 days.</param>
/// <param name="MaxRetainedEntries">The most entries the query log may hold. Defaults to 1,000,000.</param>
/// <param name="CleanupInterval">How often the retention sweep runs. Defaults to 1 hour.</param>
/// <param name="PrivacyLevel">
///     How much detail a new entry keeps, applied as each entry is written. A change takes effect
///     at once, without a restart. Defaults to full detail.
/// </param>
public record QueryLoggingSettingsDetail(
    bool Enable,
    TimeSpan RetentionPeriod,
    int MaxRetainedEntries,
    TimeSpan CleanupInterval,
    QueryLogPrivacyLevelEnum PrivacyLevel
);
