using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ZoneTransferSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ZoneTransferSettingsDetail" />, referenced by
///     <see cref="Controllers.ZoneTransferSettingsController.Get" />.
/// </summary>
public class ZoneTransferSettingsDetailExample : IExamplesProvider<ZoneTransferSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in default (see
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Options.ZoneTransferOptions" />), not an arbitrary
    ///     value.
    /// </summary>
    public ZoneTransferSettingsDetail GetExamples() => new(100);
}
