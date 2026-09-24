using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientSubnet;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientSubnetSettingsDetail" />, referenced by
///     <see cref="Controllers.ClientSubnetSettingsController.Get" />.
/// </summary>
public class ClientSubnetSettingsDetailExample : IExamplesProvider<ClientSubnetSettingsDetail>
{
    /// <summary>
    ///     This method returns this feature's own compiled-in defaults: disabled (its own real,
    ///     direct privacy cost means an operator must opt in explicitly), with the conventional
    ///     /24 (IPv4) and /56 (IPv6) disclosure prefixes and no fixed override address.
    /// </summary>
    public ClientSubnetSettingsDetail GetExamples() => new(false, 24, 56, null);
}
