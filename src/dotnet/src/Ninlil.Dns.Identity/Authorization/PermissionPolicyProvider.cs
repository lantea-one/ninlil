using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authorization;

/// <summary>
///     This class lets Ninlil authorize an endpoint against any permission string at all --
///     <c>[Authorize(Policy = "Permission:zones:write")]</c>, <c>"Permission:dhcp:read"</c>, and
///     so on -- without registering a separate, hand-written policy for every permission that
///     will ever exist. When asked for a policy name starting with <see cref="PolicyPrefix" />,
///     it builds one on the spot, requiring a <see cref="PermissionRequirement" /> for whatever
///     follows the prefix; every other policy name is delegated to the default provider, so
///     ordinary named policies keep working exactly as they would without this class.
/// </summary>
/// <param name="options">The application's configured authorization options.</param>
public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    /// <summary>
    ///     This constant contains the prefix a policy name must start with to be recognized as a
    ///     dynamically-built permission policy.
    /// </summary>
    public const string PolicyPrefix = "Permission:";

    /// <summary>
    ///     This property contains the standard provider every policy name not starting with
    ///     <see cref="PolicyPrefix" /> is delegated to.
    /// </summary>
    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider = new(options);

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetPolicyAsync(string policyName)
    {
        // Delegate anything that isn't a permission policy name to the default provider.
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            return _fallbackProvider.GetPolicyAsync(policyName);

        // Build a policy on the spot, requiring the permission named after the prefix.
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
