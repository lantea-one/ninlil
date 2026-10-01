using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.QueryLog;

/// <summary>
///     This class supplies the Swagger example for <see cref="QueryLoggingSettingsDetail" />, as
///     returned by <see cref="Controllers.QueryLoggingSettingsController.Get" />.
/// </summary>
public class QueryLoggingSettingsDetailExample : IExamplesProvider<QueryLoggingSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: logging on, 30 days or 1,000,000 entries kept, an
    ///     hourly cleanup, and full detail.
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
