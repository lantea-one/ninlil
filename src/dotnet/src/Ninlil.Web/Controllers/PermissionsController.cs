using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.Permissions;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller lists every permission string the API enforces. It finds them by
///     reflecting over each <see cref="RequirePermissionAttribute" /> in this assembly, so a new
///     controller's permissions appear without any change here. The role and user pages use it
///     to show a checklist of permissions instead of asking the operator to type them.
///     <para>
///         <b>It needs only a signed-in caller, not a specific permission.</b> It returns only
///         the permission names compiled into this assembly, which grant nothing by themselves.
///         The roles page (<c>roles:read</c>) and the users page (<c>users:read</c>) both call
///         it, and <see cref="RequirePermissionAttribute" /> can only require all of several
///         permissions, not any one of them, so gating on either would block the other page.
///     </para>
/// </summary>
[ApiController]
[Route("api/permissions")]
[Authorize]
public class PermissionsController : ControllerBase
{
    /// <summary>
    ///     This action returns the sorted, distinct permission strings named by every
    ///     <see cref="RequirePermissionAttribute" /> on a controller class or action. Requires only
    ///     a signed-in caller.
    /// </summary>
    [HttpGet]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(PermissionListExample))]
    public ActionResult<IReadOnlyList<string>> List()
    {
        Assembly controllerAssembly = typeof(PermissionsController).Assembly;

        SortedSet<string> permissions = new(StringComparer.Ordinal);

        foreach (Type candidateType in controllerAssembly.GetTypes())
        {
            if (!typeof(ControllerBase).IsAssignableFrom(candidateType))
                continue;

            foreach (
                RequirePermissionAttribute attribute in candidateType.GetCustomAttributes<RequirePermissionAttribute>(
                    inherit: true
                )
            )
                AddPermissionFromAttribute(permissions, attribute);

            foreach (
                MethodInfo action in candidateType.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            foreach (
                RequirePermissionAttribute attribute in action.GetCustomAttributes<RequirePermissionAttribute>(
                    inherit: true
                )
            )
                AddPermissionFromAttribute(permissions, attribute);
        }

        return Ok(permissions.ToList());
    }

    /// <summary>
    ///     This method adds the permission string an attribute was built with, removing the
    ///     <see cref="PermissionPolicyProvider.PolicyPrefix" /> that
    ///     <see cref="RequirePermissionAttribute" /> put in front of it.
    /// </summary>
    private static void AddPermissionFromAttribute(
        SortedSet<string> permissions,
        RequirePermissionAttribute attribute
    )
    {
        string policy = attribute.Policy;
        if (
            string.IsNullOrEmpty(policy)
            || !policy.StartsWith(PermissionPolicyProvider.PolicyPrefix, StringComparison.Ordinal)
        )
            return;

        permissions.Add(policy[PermissionPolicyProvider.PolicyPrefix.Length..]);
    }
}
