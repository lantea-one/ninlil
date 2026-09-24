using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Settings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="RuntimeSettingsWriteResult" /> -- the shared response body every
///     <c>IOptions</c>-backed runtime settings <c>PATCH</c> action across this REST API returns,
///     referenced by (at least) <see cref="Controllers.QueryLoggingSettingsController.Patch" />
///     and <see cref="Controllers.ReverseProxySettingsController.Patch" />.
/// </summary>
public class RuntimeSettingsWriteResultExample : IExamplesProvider<RuntimeSettingsWriteResult>
{
    /// <summary>
    ///     This method returns this record's own standard, shared restart-required note, exactly
    ///     as every converted runtime settings <c>PATCH</c> action actually returns it.
    /// </summary>
    public RuntimeSettingsWriteResult GetExamples() =>
        new(RuntimeSettingsWriteResult.RestartRequiredNote);
}
