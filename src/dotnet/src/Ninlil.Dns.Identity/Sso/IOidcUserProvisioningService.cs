using System.Security.Claims;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Sso;

/// <summary>
///     This interface finds or creates the local <see cref="UserEntity" /> for a completed OpenID
///     Connect sign-in, given the <see cref="ClaimsPrincipal" /> the identity provider vouched
///     for. <see cref="OidcUserProvisioningService" /> implements it and describes its
///     conservative provisioning rules.
/// </summary>
public interface IOidcUserProvisioningService
{
    /// <summary>
    ///     This method finds the local account linked to this OpenID Connect subject, or creates one
    ///     on the subject's first sign-in.
    /// </summary>
    /// <param name="principal">
    ///     The identity an OpenID Connect sign-in authenticated, typically <c>context.Principal</c> in
    ///     the <c>OpenIdConnectOptions.Events.OnTokenValidated</c> event.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching or newly created local account.</returns>
    Task<UserEntity> FindOrProvisionUserAsync(
        ClaimsPrincipal principal,
        CancellationToken stoppingToken = default
    );
}
