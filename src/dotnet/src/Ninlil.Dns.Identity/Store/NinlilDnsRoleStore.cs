using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Identity.Store;

/// <summary>
///     This class connects the ASP.NET Core Identity <see cref="RoleManager{TRole}" /> to the
///     storage-agnostic <see cref="RoleEntity" /> repository. It implements
///     <see cref="IRoleClaimStore{TRole}" />, not just <see cref="IRoleStore{TRole}" />, because
///     permissions are claims granted to a role (<see cref="RoleClaimEntity" />).
/// </summary>
/// <param name="roleRepository">The repository this store reads roles from and writes roles to.</param>
/// <param name="unitOfWork">The unit of work this store commits its changes through.</param>
public class NinlilDnsRoleStore(IRoleRepository roleRepository, IUnitOfWork unitOfWork)
    : IRoleClaimStore<RoleEntity>
{
    /// <inheritdoc />
    public Task<string> GetRoleIdAsync(RoleEntity role, CancellationToken stoppingToken) =>
        Task.FromResult(role.Id.ToString());

    /// <inheritdoc />
    public Task<string> GetRoleNameAsync(RoleEntity role, CancellationToken stoppingToken) =>
        Task.FromResult(role.Name);

    /// <inheritdoc />
    public Task SetRoleNameAsync(RoleEntity role, string roleName, CancellationToken stoppingToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> GetNormalizedRoleNameAsync(
        RoleEntity role,
        CancellationToken stoppingToken
    ) => Task.FromResult(role.NormalizedName);

    /// <inheritdoc />
    public Task SetNormalizedRoleNameAsync(
        RoleEntity role,
        string normalizedName,
        CancellationToken stoppingToken
    )
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> CreateAsync(RoleEntity role, CancellationToken stoppingToken)
    {
        role.Id = role.Id == Guid.Empty ? Guid.NewGuid() : role.Id;
        role.CreatedAtUtc = DateTimeOffset.UtcNow;

        await roleRepository.AddAsync(role, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> UpdateAsync(RoleEntity role, CancellationToken stoppingToken)
    {
        role.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // Identity calls UpdateAsync after staging a claim grant or revocation, so this also
        // saves those.
        roleRepository.Update(role);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> DeleteAsync(RoleEntity role, CancellationToken stoppingToken)
    {
        roleRepository.Remove(role);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public Task<RoleEntity> FindByIdAsync(string roleId, CancellationToken stoppingToken) =>
        Guid.TryParse(roleId, out Guid id)
            ? roleRepository.GetByIdAsync(id, stoppingToken)
            : Task.FromResult<RoleEntity>(null);

    /// <inheritdoc />
    public Task<RoleEntity> FindByNameAsync(
        string normalizedRoleName,
        CancellationToken stoppingToken
    ) => roleRepository.FindByNormalizedNameAsync(normalizedRoleName, stoppingToken);

    /// <inheritdoc />
    public async Task<IList<Claim>> GetClaimsAsync(RoleEntity role, CancellationToken stoppingToken)
    {
        IReadOnlyList<RoleClaimEntity> claims = await roleRepository.ListClaimsAsync(
            role.Id,
            stoppingToken
        );

        return [.. claims.Select(claim => new Claim(claim.ClaimType, claim.ClaimValue))];
    }

    /// <inheritdoc />
    public Task AddClaimAsync(RoleEntity role, Claim claim, CancellationToken stoppingToken) =>
        roleRepository.AddClaimAsync(role.Id, claim.Type, claim.Value, stoppingToken);

    /// <inheritdoc />
    public Task RemoveClaimAsync(RoleEntity role, Claim claim, CancellationToken stoppingToken) =>
        roleRepository.RemoveClaimAsync(role.Id, claim.Type, claim.Value, stoppingToken);

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to dispose; the container owns the repository and unit of work.
    }
}
