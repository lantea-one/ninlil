using Microsoft.AspNetCore.Authorization;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authorization;

/// <summary>
///     This class describes a single permission an authorization policy requires the current
///     caller to hold -- for example, <c>zones:write</c>.
/// </summary>
/// <param name="Permission">The permission that must be present as a claim.</param>
public record PermissionRequirement(string Permission) : IAuthorizationRequirement;
