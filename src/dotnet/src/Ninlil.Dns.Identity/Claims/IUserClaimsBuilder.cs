using System.Security.Claims;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Claims;

/// <summary>
///     This interface builds the full set of claims a signed-in <see cref="UserEntity" /> should
///     be recognized by: their identity, the roles they hold, and every permission those roles
///     (and any claims granted to the user directly) add up to. Both a JSON Web Token and an
///     API-token-authenticated request end up with the same claims through this one shared
///     implementation, so authorization behaves identically no matter which credential a caller
///     used.
/// </summary>
public interface IUserClaimsBuilder
{
    /// <summary>
    ///     This method builds the claims for a user.
    /// </summary>
    /// <param name="user">The user to build claims for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The user's identity, role, and effective permission claims.</returns>
    Task<IReadOnlyList<Claim>> BuildClaimsAsync(
        UserEntity user,
        CancellationToken stoppingToken = default
    );
}
