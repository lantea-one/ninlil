using Microsoft.AspNetCore.Authorization;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authorization;

/// <summary>
///     This class satisfies a <see cref="PermissionRequirement" /> when the caller has a matching
///     <see cref="NinlilDnsClaimTypes.Permission" /> claim, which
///     <see cref="Claims.IUserClaimsBuilder" /> adds at sign-in from the caller's roles or direct
///     claims. A claim with the value <see cref="WildcardPermission" /> satisfies every
///     requirement; the bootstrap administrator on a fresh install relies on it to reach every
///     endpoint.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>
    ///     This constant contains the permission claim value that satisfies every
    ///     <see cref="PermissionRequirement" />.
    /// </summary>
    public const string WildcardPermission = "*";

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement
    )
    {
        if (
            context.User.HasClaim(claim =>
                claim.Type == NinlilDnsClaimTypes.Permission
                && (claim.Value == requirement.Permission || claim.Value == WildcardPermission)
            )
        )
            context.Succeed(requirement);

        // Not calling Fail() leaves room for another handler of the same requirement to
        // succeed it.
        return Task.CompletedTask;
    }
}
