using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ZoneTransferClientSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="ZoneTransferClientSettingsDetail" />,
///     as returned by <see cref="Controllers.ZoneTransferClientSettingsController.Get" />.
/// </summary>
public class ZoneTransferClientSettingsDetailExample
    : IExamplesProvider<ZoneTransferClientSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults of
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Transfer.ZoneTransferClientOptions" />.
    /// </summary>
    public ZoneTransferClientSettingsDetail GetExamples() =>
        new(53, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3));
}
