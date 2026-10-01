using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Permissions;

/// <summary>
///     This class supplies the Swagger example for the sorted permission list returned by
///     <see cref="Controllers.PermissionsController.List" />.
/// </summary>
public class PermissionListExample : IExamplesProvider<IReadOnlyList<string>>
{
    /// <summary>
    ///     This method returns a sorted sample of the catalog, which is too long to show in full,
    ///     illustrating the <c>resource:read</c> and <c>resource:write</c> naming (some resources
    ///     also have <c>resource:manage</c>).
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
