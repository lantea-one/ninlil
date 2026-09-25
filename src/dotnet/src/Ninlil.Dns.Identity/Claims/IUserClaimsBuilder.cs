using System.Security.Claims;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Claims;

/// <summary>
///     This interface builds the claims of a signed-in <see cref="UserEntity" />: identity, roles,
///     and every permission from those roles and from claims granted directly. JWTs and
///     API-token requests both get their claims here, so authorization works the same whichever
///     credential was used.
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
