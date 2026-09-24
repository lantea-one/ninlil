using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Dns64;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Dns64SettingsDetail" />, referenced by
///     <see cref="Controllers.Dns64SettingsController.Get" />.
/// </summary>
public class Dns64SettingsDetailExample : IExamplesProvider<Dns64SettingsDetail>
{
    /// <summary>
    ///     This method returns this feature's own compiled-in defaults: disabled (synthesizing a
    ///     record nobody actually published is a capability-expanding change, not a pure
    ///     hardening one, so an operator must opt in explicitly), with RFC 6052's own Well-Known
    ///     Prefix.
    /// </summary>
    public Dns64SettingsDetail GetExamples() => new(false, "64:ff9b::/96");
}
