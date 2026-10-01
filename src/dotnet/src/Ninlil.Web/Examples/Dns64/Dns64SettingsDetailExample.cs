using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Dns64;

/// <summary>
///     This class supplies the Swagger example for <see cref="Dns64SettingsDetail" />, as returned
///     by <see cref="Controllers.Dns64SettingsController.Get" />.
/// </summary>
public class Dns64SettingsDetailExample : IExamplesProvider<Dns64SettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: DNS64 off, because synthesizing records no zone
    ///     published must be opted into, with the RFC 6052 Well-Known Prefix.
    /// </summary>
    public Dns64SettingsDetail GetExamples() => new(false, "64:ff9b::/96");
}
