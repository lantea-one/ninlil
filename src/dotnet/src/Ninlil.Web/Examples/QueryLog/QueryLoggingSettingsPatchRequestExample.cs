using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.QueryLog;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.QueryLoggingSettingsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field from a present
///     one (see <see cref="Rewrites.DnsRewritePatchRequestExample" />).
/// </summary>
public class QueryLoggingSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that shortens retention to 14 days and anonymizes client
    ///     addresses. Because it changes <c>privacyLevel</c> with a restart-only field, the
    ///     response carries
    ///     <see cref="Models.RuntimeSettingsWriteResult.QueryLoggingMixedReloadNote" />.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse("""{ "retentionPeriod": "14.00:00:00", "privacyLevel": "ClientAnonymized" }""")
            .RootElement.Clone();
}
