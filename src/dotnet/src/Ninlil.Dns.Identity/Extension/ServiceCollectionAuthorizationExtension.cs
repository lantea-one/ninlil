using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Identity.Authorization;

// Define our namespace.
namespace Ninlil.Dns.Identity.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's permission-based policy authorization.
/// </summary>
public static class ServiceCollectionAuthorizationExtension
{
    /// <summary>
    ///     This method registers authorization services along with
    ///     <see cref="PermissionPolicyProvider" /> and <see cref="PermissionAuthorizationHandler" />,
    ///     so any endpoint can be protected with <c>[RequirePermission("...")]</c> without that
    ///     permission needing to be registered as a named policy ahead of time.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsAuthorization(this IServiceCollection instance)
    {
        instance.AddAuthorization();
        instance.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        instance.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
