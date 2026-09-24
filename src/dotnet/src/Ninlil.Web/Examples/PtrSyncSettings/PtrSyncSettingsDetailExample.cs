using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.PtrSyncSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="PtrSyncSettingsDetail" />, referenced by
///     <see cref="Controllers.PtrSyncSettingsController.Get" />.
/// </summary>
public class PtrSyncSettingsDetailExample : IExamplesProvider<PtrSyncSettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Ninlil.Dns.Server.Authoritative.Options.PtrSyncOptions" />'s own doc comments for why
    ///     each one defaults where it does), not arbitrary values.
    /// </summary>
    public PtrSyncSettingsDetail GetExamples() => new(false, 24, 64);
}
