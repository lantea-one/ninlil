using Microsoft.AspNetCore.Authorization;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authorization;

/// <summary>
///     This class satisfies a <see cref="PermissionRequirement" /> when the current caller
///     carries a matching <see cref="NinlilDnsClaimTypes.Permission" /> claim -- placed there,
///     at sign-in, by <see cref="Claims.IUserClaimsBuilder" /> from either the roles the caller
///     holds or a claim granted to them directly. A permission claim whose value is
///     <see cref="WildcardPermission" /> satisfies every requirement, not just one named
///     <c>*</c> literally -- the mechanism the bootstrap administrator account (the
///     very first account on a fresh install, before any other permission has ever been granted)
///     relies on to reach every endpoint despite no specific permission strings existing yet.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>
    ///     This constant contains the permission claim value that satisfies every
    ///     <see cref="PermissionRequirement" />, regardless of which specific permission it
    ///     names.
    /// </summary>
    public const string WildcardPermission = "*";

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement
    )
    {
        // Check whether the caller carries a permission claim with the exact value required, or
        // the wildcard permission that stands in for every requirement at once.
        if (
            context.User.HasClaim(claim =>
                claim.Type == NinlilDnsClaimTypes.Permission
                && (claim.Value == requirement.Permission || claim.Value == WildcardPermission)
            )
        )
            context.Succeed(requirement);

        // We're done. Leaving the requirement unsatisfied here (rather than explicitly failing)
        // lets another handler for the same requirement, if one is ever added, still succeed it.
        return Task.CompletedTask;
    }
}
