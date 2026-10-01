using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.PtrSyncSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="PtrSyncSettingsDetail" />, as
///     returned by <see cref="Controllers.PtrSyncSettingsController.Get" />.
/// </summary>
public class PtrSyncSettingsDetailExample : IExamplesProvider<PtrSyncSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults of
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Options.PtrSyncOptions" />: PTR sync off, /24
    ///     IPv4 and /64 IPv6 reverse zones.
    /// </summary>
    public PtrSyncSettingsDetail GetExamples() => new(false, 24, 64);
}
