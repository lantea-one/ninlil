using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientSubnet;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientSubnetSettingsDetail" />, as
///     returned by <see cref="Controllers.ClientSubnetSettingsController.Get" />.
/// </summary>
public class ClientSubnetSettingsDetailExample : IExamplesProvider<ClientSubnetSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: ECS off, because it discloses part of each client's
    ///     address upstream and must be opted into, with /24 and /56 source prefixes and no
    ///     override address.
    /// </summary>
    public ClientSubnetSettingsDetail GetExamples() => new(false, 24, 56, null);
}
