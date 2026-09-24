using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.QueryLog;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="QueryLoggingSettingsDetail" />, referenced by
///     <see cref="Controllers.QueryLoggingSettingsController.Get" />.
/// </summary>
public class QueryLoggingSettingsDetailExample : IExamplesProvider<QueryLoggingSettingsDetail>
{
    /// <summary>
    ///     This method returns this feature's own real, on-by-default settings, at a plausible
    ///     operator-tuned retention period and this scale's own default privacy level (full
    ///     detail, unchanged from before this level even existed).
    /// </summary>
    public QueryLoggingSettingsDetail GetExamples() =>
        new(
            true,
            TimeSpan.FromDays(30),
            1_000_000,
            TimeSpan.FromHours(1),
            QueryLogPrivacyLevelEnum.Full
        );
}
