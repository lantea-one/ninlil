using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Authorization;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="ClaimsPrincipal" /> extensions for the rule that a
///     caller can grant only permissions it holds itself. Without the rule, anyone with
///     <c>roles:write</c> or <c>users:write</c> could give themselves <c>*</c> and so reach every
///     endpoint. <c>RolesController</c> and <c>UsersController</c> call it before they add a
///     permission to a role or user, or add a user to a role. Removing a permission is not
///     restricted.
/// </summary>
public static class PermissionGrantExtension
{
    /// <summary>
    ///     This method returns the permissions in <paramref name="requested" /> that the caller
    ///     does not hold. The caller holds a permission when it has a matching permission claim,
    ///     or the <see cref="PermissionAuthorizationHandler.WildcardPermission" /> claim, which
    ///     covers every permission including itself. The wildcard itself is covered only by the
    ///     wildcard claim.
    /// </summary>
    /// <param name="instance">The caller, as authenticated for this request.</param>
    /// <param name="requested">The permission strings the caller wants to grant.</param>
    /// <returns>The permissions the caller may not grant; empty when the grant is allowed.</returns>
    public static IReadOnlyList<string> GetUnheldPermissions(
        this ClaimsPrincipal instance,
        IEnumerable<string> requested
    )
    {
        HashSet<string> held = instance
            .FindAll(NinlilDnsClaimTypes.Permission)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        if (held.Contains(PermissionAuthorizationHandler.WildcardPermission))
            return [];

        return requested.Where(permission => !held.Contains(permission)).Distinct().ToList();
    }

    /// <summary>
    ///     This method builds the 403 response for a refused grant, naming the permissions the
    ///     caller lacks, or returns <see langword="null" /> when the grant is allowed.
    /// </summary>
    /// <param name="instance">The controller handling the request.</param>
    /// <param name="requested">The permission strings the caller wants to grant.</param>
    /// <returns>A 403 <see cref="ProblemDetails" /> result, or <see langword="null" />.</returns>
    public static ObjectResult RefuseUnheldPermissions(
        this ControllerBase instance,
        IEnumerable<string> requested
    )
    {
        IReadOnlyList<string> missing = instance.User.GetUnheldPermissions(requested);
        if (missing.Count == 0)
            return null;

        return instance.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden.",
            detail: "You can grant only permissions you hold yourself. Missing: "
                + string.Join(", ", missing.Select(permission => $"'{permission}'"))
                + "."
        );
    }
}
