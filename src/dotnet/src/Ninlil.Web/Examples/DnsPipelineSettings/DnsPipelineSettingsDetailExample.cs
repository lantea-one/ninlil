using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsPipelineSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsPipelineSettingsDetail" />, referenced by
///     <see cref="Controllers.DnsPipelineSettingsController.Get" />.
/// </summary>
public class DnsPipelineSettingsDetailExample : IExamplesProvider<DnsPipelineSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipelineOptions" />), not arbitrary
    ///     values. <c>WorkerCount</c> is shown as 8 here since the real compiled-in default is
    ///     <see cref="Environment.ProcessorCount" />, which varies by machine.
    /// </summary>
    public DnsPipelineSettingsDetail GetExamples() => new(8, 4096, 1232, 20, 512);
}
