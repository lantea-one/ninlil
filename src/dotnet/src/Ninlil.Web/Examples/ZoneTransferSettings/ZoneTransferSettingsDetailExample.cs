using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ZoneTransferSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="ZoneTransferSettingsDetail" />, as
///     returned by <see cref="Controllers.ZoneTransferSettingsController.Get" />.
/// </summary>
public class ZoneTransferSettingsDetailExample : IExamplesProvider<ZoneTransferSettingsDetail>
{
    /// <summary>
    ///     This method returns the default of
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Options.ZoneTransferOptions" />: 100 records
    ///     per message.
    /// </summary>
    public ZoneTransferSettingsDetail GetExamples() => new(100);
}
