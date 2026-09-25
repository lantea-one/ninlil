using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authorization;

/// <summary>
///     This class lets an endpoint be authorized against any permission string
///     (<c>[Authorize(Policy = "Permission:zones:write")]</c>, <c>"Permission:dhcp:read"</c>, and
///     so on) without registering a policy per permission. For a policy name starting with
///     <see cref="PolicyPrefix" /> it builds a policy requiring a
///     <see cref="PermissionRequirement" /> for the rest of the name. Other names go to the
///     default provider, so ordinary named policies still work. <see cref="RequirePermissionAttribute" />
///     builds these names.
/// </summary>
/// <param name="options">The application's configured authorization options.</param>
public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    /// <summary>
    ///     This constant contains the prefix that marks a policy name as a permission policy.
    /// </summary>
    public const string PolicyPrefix = "Permission:";

    /// <summary>
    ///     This field contains the default provider that handles every other policy name.
    /// </summary>
    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider = new(options);

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            return _fallbackProvider.GetPolicyAsync(policyName);

        string permission = policyName[PolicyPrefix.Length..];

        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();

        return Task.FromResult(policy);
    }

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallbackProvider.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetFallbackPolicyAsync() =>
        _fallbackProvider.GetFallbackPolicyAsync();
}
