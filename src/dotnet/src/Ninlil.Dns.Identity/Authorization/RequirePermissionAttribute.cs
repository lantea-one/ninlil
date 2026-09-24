using Microsoft.AspNetCore.Authorization;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authorization;

/// <summary>
///     This attribute authorizes an endpoint against a single named permission, such as
///     <c>[RequirePermission("zones:write")]</c>, without the caller needing to know or spell out
///     <see cref="PermissionPolicyProvider.PolicyPrefix" /> themselves.
/// </summary>
/// <param name="permission">The permission the caller must hold.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute(string permission)
    : AuthorizeAttribute(PermissionPolicyProvider.PolicyPrefix + permission);
