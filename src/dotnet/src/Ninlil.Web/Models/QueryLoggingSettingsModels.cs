using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>QueryLoggingOptions</c>. No secret material is involved, so this record's own fields
///     are the exact same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
/// <param name="Enable">Whether an already-answered query is persisted to the query log at all.</param>
/// <param name="RetentionPeriod">How long a query log entry is kept before it ages out.</param>
/// <param name="MaxRetainedEntries">The largest number of entries the query log is ever allowed to hold.</param>
/// <param name="CleanupInterval">How often the retention sweep runs.</param>
/// <param name="PrivacyLevel">
///     How much detail a newly-answered query is actually allowed to carry into storage --
///     applied at write time, live, with no restart required.
/// </param>
public record QueryLoggingSettingsDetail(
    bool Enable,
    TimeSpan RetentionPeriod,
    int MaxRetainedEntries,
    TimeSpan CleanupInterval,
    QueryLogPrivacyLevelEnum PrivacyLevel
);
