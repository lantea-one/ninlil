using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="TsigSettingsDetail" />, as returned
///     by <see cref="Controllers.TsigSettingsController.Get" />.
/// </summary>
public class TsigSettingsDetailExample : IExamplesProvider<TsigSettingsDetail>
{
    /// <summary>
    ///     This method returns the default fudge of 300 seconds from
    ///     <see cref="Ninlil.Dns.Server.Tsig.Options.TsigServiceOptions" />, the value RFC 8945
    ///     recommends.
    /// </summary>
    public TsigSettingsDetail GetExamples() => new(300);
}
