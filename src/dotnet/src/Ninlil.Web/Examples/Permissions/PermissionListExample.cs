using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Permissions;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for the sorted list
///     of permission strings <see cref="Controllers.PermissionsController.List" /> returns.
/// </summary>
public class PermissionListExample : IExamplesProvider<IReadOnlyList<string>>
{
    /// <summary>
    ///     This method returns a representative subset of the real, sorted permission catalog --
    ///     not every permission string this project registers, since the full set is large and
    ///     grows automatically as new controllers are added; this sample is illustrative of the
    ///     <c>resource:read</c>/<c>resource:write</c> naming convention every permission follows.
    /// </summary>
    public IReadOnlyList<string> GetExamples() =>
        [
            "backup:read",
            "backup:write",
            "filterlists:read",
            "filterlists:write",
            "forwarders:read",
            "forwarders:write",
            "roles:read",
            "roles:write",
            "status:read",
            "users:read",
            "users:write",
            "zones:read",
            "zones:write",
        ];
}
