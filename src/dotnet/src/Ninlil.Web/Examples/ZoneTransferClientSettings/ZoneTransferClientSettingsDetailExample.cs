using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ZoneTransferClientSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ZoneTransferClientSettingsDetail" />, referenced by
///     <see cref="Controllers.ZoneTransferClientSettingsController.Get" />.
/// </summary>
public class ZoneTransferClientSettingsDetailExample
    : IExamplesProvider<ZoneTransferClientSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Transfer.ZoneTransferClientOptions" />), not arbitrary
    ///     values.
    /// </summary>
    public ZoneTransferClientSettingsDetail GetExamples() =>
        new(53, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3));
}
