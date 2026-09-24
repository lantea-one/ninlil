using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.Permissions;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller enumerates every permission-claim string this server actually enforces
///     anywhere, computed by reflecting over every <see cref="RequirePermissionAttribute" />
///     usage across this project's own controller assembly -- never a hand-maintained list, so a
///     future controller's own new permission is picked up automatically with zero changes here.
///     Built for the role- and user-management pages, both of which need to render a
///     checkbox list of every known permission string rather than asking an operator to type one
///     from memory.
///     <para>
///         <b>Gated by <see cref="AuthorizeAttribute" /> alone, not a specific permission.</b>
///         This was a real, considered choice, not an oversight: the endpoint exposes no secret
///         or configuration data at all -- only the same permission-string identifiers already
///         baked into this assembly's own attribute usages -- and possessing it grants no
///         capability by itself. Both <c>RolesController</c>'s own permission-picker (naturally
///         gated by <c>roles:read</c>) and <c>UsersController</c>'s own direct-permission-picker
///         (naturally gated by <c>users:read</c>) need to call this same endpoint; picking either
///         one of those two permissions to gate this endpoint on would arbitrarily block the
///         other page's own legitimate use of it, and there is no clean way to express "either
///         of these two permissions" with <see cref="RequirePermissionAttribute" />'s own
///         AND-composing, stacked-<c>[Authorize]</c> semantics. Requiring only a valid,
///         authenticated session -- the same bar <c>ApiTokensController</c>/
///         <c>TwoFactorController</c> already set for other harmless, self-service-shaped reads
///         -- was judged the correct, minimal gate.
///     </para>
/// </summary>
[ApiController]
[Route("api/permissions")]
[Authorize]
public class PermissionsController : ControllerBase
{
    /// <summary>
    ///     This action returns the distinct, sorted set of every permission string named by any
    ///     <see cref="RequirePermissionAttribute" /> applied anywhere in this project's own
    ///     controller assembly -- on a controller class itself or on any individual action.
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
    ///     This method recovers the raw permission string an attribute was constructed with,
    ///     stripping <see cref="PermissionPolicyProvider.PolicyPrefix" /> back off the
    ///     <see cref="AuthorizeAttribute.Policy" /> value <see cref="RequirePermissionAttribute" />
    ///     baked it into.
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
