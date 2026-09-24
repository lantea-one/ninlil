using System.Security.Claims;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Sso;

/// <summary>
///     This interface finds or creates the local <see cref="UserEntity" /> matching an
///     already-authenticated OpenID Connect sign-in, given the <see cref="ClaimsPrincipal" /> the
///     identity provider's own token endpoint vouched for. See
///     <see cref="OidcUserProvisioningService" /> for the real implementation and its own
///     deliberately conservative auto-provisioning policy.
/// </summary>
public interface IOidcUserProvisioningService
{
    /// <summary>
    ///     This method finds the local account already linked to this OpenID Connect subject, or
    ///     provisions a brand-new one if this is that subject's first sign-in.
    /// </summary>
    /// <param name="principal">
    ///     The authenticated identity an OpenID Connect handshake just completed for -- typically
    ///     the <c>OpenIdConnectOptions.Events.OnTokenValidated</c> event's own <c>context.Principal</c>.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching, or newly-provisioned, local account.</returns>
    Task<UserEntity> FindOrProvisionUserAsync(
        ClaimsPrincipal principal,
        CancellationToken stoppingToken = default
    );
}
