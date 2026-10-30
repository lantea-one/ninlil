using System.Security.Claims;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authentication;

/// <summary>
///     This interface checks an already issued JWT access token against the account it names, on
///     every request. A signed token is valid until it expires, so without this check a disabled
///     account, a deleted account or a revoked permission would keep working for the rest of the
///     token's lifetime (<see cref="Options.JwtOptions.AccessTokenLifetime" />). The JWT bearer
///     scheme calls <see cref="RevalidateAsync" /> after it has validated the token's signature
///     (see <see cref="Extension.ServiceCollectionAuthenticationExtension" />); the identity stores
///     and the cluster sync appliers call <see cref="Invalidate" /> after they change an account,
///     a role or a permission.
/// </summary>
public interface IAccessTokenRevalidator
{
    /// <summary>
    ///     This method checks that the account in the token's <c>sub</c> claim exists, is enabled
    ///     and is not locked out, and replaces the principal's role and permission claims with the
    ///     account's current ones, so a permission granted or revoked after the token was issued
    ///     applies to it. The answer comes from a short-lived cache, so a warm request reads no
    ///     database.
    /// </summary>
    /// <param name="principal">The principal the JWT bearer scheme built from the token. Its role and permission claims are replaced in place.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> when the request may go on; <see langword="false" /> when the token must be rejected with 401.</returns>
    Task<bool> RevalidateAsync(
        ClaimsPrincipal principal,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method discards every cached answer, so the next request of every account reads the
    ///     database again. Callers invoke it after the change is saved, so a read that follows
    ///     cannot see the old data. Changes to users and roles are rare, so one flush for all
    ///     accounts is cheaper than tracking which accounts a role change reaches.
    /// </summary>
    void Invalidate();
}
