using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Identity.Claims;

/// <summary>
///     This class implements <see cref="IUserClaimsBuilder" /> by combining a user's own record
///     with the roles they hold and the permission claims those roles -- and the user directly --
///     have been granted.
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
        // Look up the roles this user holds, so both the role names and the permissions those
        // roles grant can be turned into claims.
        IReadOnlyList<RoleEntity> roles = await userRepository.ListRolesAsync(
            user.Id,
            stoppingToken
        );

        // Gather every permission granted by any of those roles.
        List<string> permissionsFromRoles = [];
        foreach (RoleEntity role in roles)
        {
            IReadOnlyList<RoleClaimEntity> roleClaims = await roleRepository.ListClaimsAsync(
                role.Id,
                stoppingToken
            );

            permissionsFromRoles.AddRange(
                roleClaims
                    .Where(claim => claim.ClaimType == NinlilDnsClaimTypes.Permission)
                    .Select(claim => claim.ClaimValue)
            );
        }

        // Gather every permission granted to the user directly, independent of their roles.
        IReadOnlyList<UserClaimEntity> userClaims = await userRepository.ListClaimsAsync(
            user.Id,
            stoppingToken
        );

        IEnumerable<string> permissionsFromUser = userClaims
            .Where(claim => claim.ClaimType == NinlilDnsClaimTypes.Permission)
            .Select(claim => claim.ClaimValue);

        // Combine both sources into the final, de-duplicated set of effective permissions.
        HashSet<string> effectivePermissions = [.. permissionsFromRoles, .. permissionsFromUser];

        // Build the identity claims every signed-in user carries.
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
        ];

        // Only include the e-mail claim once the address has actually been confirmed, so a
        // caller never asserts an unverified e-mail address as fact.
        if (user.EmailConfirmed && !string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));

        // Add one role claim per role, using the standard .NET role claim type so that
        // [Authorize(Roles = "...")] recognizes it without any extra configuration.
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role.Name)));

        // Add one permission claim per effective permission.
        claims.AddRange(
            effectivePermissions.Select(permission => new Claim(
                NinlilDnsClaimTypes.Permission,
                permission
            ))
        );

        // We're done, return the completed claim set.
        return claims;
    }
}
