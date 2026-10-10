using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Identity.Claims;

/// <summary>
///     This class implements <see cref="IUserClaimsBuilder" /> by combining a user's record, the
///     roles they hold, and the permission claims granted through those roles or directly.
/// </summary>
/// <param name="userRepository">The repository used to look up a user's roles and claims.</param>
/// <param name="roleRepository">The repository used to look up the claims granted by a role.</param>
public class UserClaimsBuilder(IUserRepository userRepository, IRoleRepository roleRepository)
    : IUserClaimsBuilder
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Claim>> BuildClaimsAsync(
        UserEntity user,
        CancellationToken stoppingToken = default
    )
    {
        IReadOnlyList<RoleEntity> roles = await userRepository.ListRolesAsync(
            user.Id,
            stoppingToken
        );

        // One query for the claims of all the user's roles, not one per role.
        IReadOnlyList<RoleClaimEntity> roleClaims = await roleRepository.ListClaimsForRolesAsync(
            [.. roles.Select(role => role.Id)],
            stoppingToken
        );

        IEnumerable<string> permissionsFromRoles = roleClaims
            .Where(claim => claim.ClaimType == NinlilDnsClaimTypes.Permission)
            .Select(claim => claim.ClaimValue);

        IReadOnlyList<UserClaimEntity> userClaims = await userRepository.ListClaimsAsync(
            user.Id,
            stoppingToken
        );

        IEnumerable<string> permissionsFromUser = userClaims
            .Where(claim => claim.ClaimType == NinlilDnsClaimTypes.Permission)
            .Select(claim => claim.ClaimValue);

        // Role and direct permissions combined, without duplicates.
        HashSet<string> effectivePermissions = [.. permissionsFromRoles, .. permissionsFromUser];

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
        ];

        // Only a confirmed e-mail address is asserted as a claim.
        if (user.EmailConfirmed && !string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));

        // The standard role claim type makes [Authorize(Roles = "...")] work unchanged.
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role.Name)));

        claims.AddRange(
            effectivePermissions.Select(permission => new Claim(
                NinlilDnsClaimTypes.Permission,
                permission
            ))
        );

        return claims;
    }
}
