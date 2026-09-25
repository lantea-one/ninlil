using Microsoft.AspNetCore.Authorization;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authorization;

/// <summary>
///     This record describes one permission an authorization policy requires the caller to hold,
///     such as <c>zones:write</c>. <see cref="PermissionPolicyProvider" /> creates it and
///     <see cref="PermissionAuthorizationHandler" /> checks it.
/// </summary>
/// <param name="Permission">The permission that must be present as a claim.</param>
public record PermissionRequirement(string Permission) : IAuthorizationRequirement;
