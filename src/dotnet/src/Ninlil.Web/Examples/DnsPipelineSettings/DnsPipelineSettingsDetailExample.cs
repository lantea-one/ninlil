using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsPipelineSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnsPipelineSettingsDetail" />, as
///     returned by <see cref="Controllers.DnsPipelineSettingsController.Get" />.
/// </summary>
public class DnsPipelineSettingsDetailExample : IExamplesProvider<DnsPipelineSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults of
    ///     <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipelineOptions" />.
    ///     <c>WorkerCount</c> is shown as 8; the real default is
    ///     <see cref="Environment.ProcessorCount" />, which varies by machine.
    /// </summary>
    public DnsPipelineSettingsDetail GetExamples() => new(8, 4096, 1232, 20, 512);
}
