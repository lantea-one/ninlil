using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="TsigSettingsDetail" />, referenced by
///     <see cref="Controllers.TsigSettingsController.Get" />.
/// </summary>
public class TsigSettingsDetailExample : IExamplesProvider<TsigSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in default (see
    ///     <see cref="Ninlil.Dns.Server.Tsig.Options.TsigServiceOptions" />, RFC 8945's own recommended
    ///     value), not an arbitrary value.
    /// </summary>
    public TsigSettingsDetail GetExamples() => new(300);
}
