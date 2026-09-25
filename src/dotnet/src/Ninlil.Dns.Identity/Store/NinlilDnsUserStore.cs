using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Identity.Store;

/// <summary>
///     This class connects ASP.NET Core Identity to the storage-agnostic <see cref="UserEntity" />
///     repository. <see cref="UserManager{TUser}" /> never touches a database itself; it goes
///     through a store that implements the small Identity store interfaces each feature needs.
///     Because this store uses <see cref="IUserRepository" /> rather than Entity Framework Core,
///     it works with both the relational engines and MongoDB.
///     <para>
///         Most methods only get or set properties on the <see cref="UserEntity" /> Identity is
///         holding. Identity follows every change with <see cref="UpdateAsync" />, which is where
///         the store saves.
///     </para>
/// </summary>
/// <param name="userRepository">The repository this store reads users from and writes users to.</param>
/// <param name="roleRepository">The repository used to resolve role names into role identifiers.</param>
/// <param name="unitOfWork">The unit of work this store commits its changes through.</param>
/// <param name="dataProtectionProvider">
///     The provider used to encrypt a user's TOTP authenticator key before it is persisted.
/// </param>
public class NinlilDnsUserStore(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IUnitOfWork unitOfWork,
    IDataProtectionProvider dataProtectionProvider
)
    : IUserStore<UserEntity>,
        IUserPasswordStore<UserEntity>,
        IUserEmailStore<UserEntity>,
        IUserPhoneNumberStore<UserEntity>,
        IUserTwoFactorStore<UserEntity>,
        IUserLockoutStore<UserEntity>,
        IUserSecurityStampStore<UserEntity>,
        IUserClaimStore<UserEntity>,
        IUserRoleStore<UserEntity>,
        IUserAuthenticatorKeyStore<UserEntity>,
        IUserTwoFactorRecoveryCodeStore<UserEntity>
{
    /// <summary>
    ///     This constant contains the purpose string of <see cref="_authenticatorKeyProtector" />. It is
    ///     public so cluster identity sync in <c>Ninlil.Dns.Server.ClusterSync</c>
    ///     (<c>UserClusterSyncEventSubscriber</c> and <c>UserClusterConfigSyncApplier</c>) can create the
    ///     same protector on each node, decrypting a TOTP key on the sender and re-encrypting it on
    ///     the receiver, without a second copy of the string.
    /// </summary>
    public const string AuthenticatorKeyProtectorPurpose =
        "Ninlil.Dns.Identity.TwoFactor.AuthenticatorKey";

    /// <summary>
    ///     This constant contains the purpose string used to derive
    ///     <see cref="_recoveryCodesProtector" />, exposed for the same reason as
    ///     <see cref="AuthenticatorKeyProtectorPurpose" />.
    /// </summary>
    public const string RecoveryCodesProtectorPurpose =
        "Ninlil.Dns.Identity.TwoFactor.RecoveryCodes";

    /// <summary>
    ///     This field contains the protector that encrypts a user's TOTP authenticator key, so the
    ///     shared secret is never stored in plain text.
    /// </summary>
    private readonly IDataProtector _authenticatorKeyProtector =
        dataProtectionProvider.CreateProtector(AuthenticatorKeyProtectorPurpose);

    /// <summary>
    ///     This field contains the protector that encrypts a user's two-factor recovery codes, so
    ///     they are never stored in plain text. It has its own purpose string, separate from
    ///     <see cref="_authenticatorKeyProtector" />, as each secret type does.
    /// </summary>
    private readonly IDataProtector _recoveryCodesProtector =
        dataProtectionProvider.CreateProtector(RecoveryCodesProtectorPurpose);

    /// <summary>
    ///     This constant is the delimiter <see cref="ReplaceCodesAsync" /> and
    ///     <see cref="RedeemCodeAsync" /> use to join and split the recovery codes around encryption.
    ///     It matches the default Identity <c>UserStore</c> for familiarity; nothing else depends on
    ///     it.
    /// </summary>
    private const string RecoveryCodeDelimiter = ";";

    // ------------------------------------------------------------------------------------------
    // IUserStore<UserEntity>: identifier and name, and creating, updating and deleting the
    // user.
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<string> GetUserIdAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.Id.ToString());

    /// <inheritdoc />
    public Task<string> GetUserNameAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.UserName);

    /// <inheritdoc />
    public Task SetUserNameAsync(UserEntity user, string userName, CancellationToken stoppingToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> GetNormalizedUserNameAsync(
        UserEntity user,
        CancellationToken stoppingToken
    ) => Task.FromResult(user.NormalizedUserName);

    /// <inheritdoc />
    public Task SetNormalizedUserNameAsync(
        UserEntity user,
        string normalizedName,
        CancellationToken stoppingToken
    )
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> CreateAsync(UserEntity user, CancellationToken stoppingToken)
    {
        user.Id = user.Id == Guid.Empty ? Guid.NewGuid() : user.Id;
        user.CreatedAtUtc = DateTimeOffset.UtcNow;

        await userRepository.AddAsync(user, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> UpdateAsync(UserEntity user, CancellationToken stoppingToken)
    {
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // Identity calls UpdateAsync after staging changes (a claim, a role, an authenticator
        // key), so this saves those too.
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> DeleteAsync(UserEntity user, CancellationToken stoppingToken)
    {
        userRepository.Remove(user);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public Task<UserEntity> FindByIdAsync(string userId, CancellationToken stoppingToken) =>
        Guid.TryParse(userId, out Guid id)
            ? userRepository.GetByIdAsync(id, stoppingToken)
            : Task.FromResult<UserEntity>(null);

    /// <inheritdoc />
    public Task<UserEntity> FindByNameAsync(
        string normalizedUserName,
        CancellationToken stoppingToken
    ) => userRepository.FindByNormalizedUserNameAsync(normalizedUserName, stoppingToken);

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to dispose; the container owns the dependencies.
    }

    // ------------------------------------------------------------------------------------------
    // IUserPasswordStore<UserEntity>
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task SetPasswordHashAsync(
        UserEntity user,
        string passwordHash,
        CancellationToken stoppingToken
    )
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> GetPasswordHashAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.PasswordHash);

    /// <inheritdoc />
    public Task<bool> HasPasswordAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // ------------------------------------------------------------------------------------------
    // IUserEmailStore<UserEntity>
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task SetEmailAsync(UserEntity user, string email, CancellationToken stoppingToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> GetEmailAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.Email);

    /// <inheritdoc />
    public Task<bool> GetEmailConfirmedAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.EmailConfirmed);

    /// <inheritdoc />
    public Task SetEmailConfirmedAsync(
        UserEntity user,
        bool confirmed,
        CancellationToken stoppingToken
    )
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<UserEntity> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken stoppingToken
    ) => userRepository.FindByNormalizedEmailAsync(normalizedEmail, stoppingToken);

    /// <inheritdoc />
    public Task<string> GetNormalizedEmailAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.NormalizedEmail);

    /// <inheritdoc />
    public Task SetNormalizedEmailAsync(
        UserEntity user,
        string normalizedEmail,
        CancellationToken stoppingToken
    )
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------------------------------
    // IUserPhoneNumberStore<UserEntity>
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task SetPhoneNumberAsync(
        UserEntity user,
        string phoneNumber,
        CancellationToken stoppingToken
    )
    {
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> GetPhoneNumberAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.PhoneNumber);

    /// <inheritdoc />
    public Task<bool> GetPhoneNumberConfirmedAsync(
        UserEntity user,
        CancellationToken stoppingToken
    ) => Task.FromResult(user.PhoneNumberConfirmed);

    /// <inheritdoc />
    public Task SetPhoneNumberConfirmedAsync(
        UserEntity user,
        bool confirmed,
        CancellationToken stoppingToken
    )
    {
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------------------------------
    // IUserTwoFactorStore<UserEntity>
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task SetTwoFactorEnabledAsync(
        UserEntity user,
        bool enabled,
        CancellationToken stoppingToken
    )
    {
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> GetTwoFactorEnabledAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.TwoFactorEnabled);

    // ------------------------------------------------------------------------------------------
    // IUserLockoutStore<UserEntity>
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(
        UserEntity user,
        CancellationToken stoppingToken
    ) => Task.FromResult(user.LockoutEndUtc);

    /// <inheritdoc />
    public Task SetLockoutEndDateAsync(
        UserEntity user,
        DateTimeOffset? lockoutEnd,
        CancellationToken stoppingToken
    )
    {
        user.LockoutEndUtc = lockoutEnd;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> IncrementAccessFailedCountAsync(
        UserEntity user,
        CancellationToken stoppingToken
    )
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    /// <inheritdoc />
    public Task ResetAccessFailedCountAsync(UserEntity user, CancellationToken stoppingToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> GetAccessFailedCountAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.AccessFailedCount);

    /// <inheritdoc />
    public Task<bool> GetLockoutEnabledAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.LockoutEnabled);

    /// <inheritdoc />
    public Task SetLockoutEnabledAsync(
        UserEntity user,
        bool enabled,
        CancellationToken stoppingToken
    )
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------------------------------
    // IUserSecurityStampStore<UserEntity>
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task SetSecurityStampAsync(
        UserEntity user,
        string stamp,
        CancellationToken stoppingToken
    )
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> GetSecurityStampAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(user.SecurityStamp);

    // ------------------------------------------------------------------------------------------
    // IUserClaimStore<UserEntity>
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IList<Claim>> GetClaimsAsync(UserEntity user, CancellationToken stoppingToken)
    {
        IReadOnlyList<UserClaimEntity> claims = await userRepository.ListClaimsAsync(
            user.Id,
            stoppingToken
        );

        return [.. claims.Select(claim => new Claim(claim.ClaimType, claim.ClaimValue))];
    }

    /// <inheritdoc />
    public Task AddClaimsAsync(
        UserEntity user,
        IEnumerable<Claim> claims,
        CancellationToken stoppingToken
    ) =>
        userRepository.AddClaimsAsync(
            user.Id,
            claims.Select(claim => (claim.Type, claim.Value)),
            stoppingToken
        );

    /// <inheritdoc />
    public Task ReplaceClaimAsync(
        UserEntity user,
        Claim claim,
        Claim newClaim,
        CancellationToken stoppingToken
    ) =>
        userRepository.ReplaceClaimAsync(
            user.Id,
            claim.Type,
            claim.Value,
            newClaim.Type,
            newClaim.Value,
            stoppingToken
        );

    /// <inheritdoc />
    public Task RemoveClaimsAsync(
        UserEntity user,
        IEnumerable<Claim> claims,
        CancellationToken stoppingToken
    ) =>
        userRepository.RemoveClaimsAsync(
            user.Id,
            claims.Select(claim => (claim.Type, claim.Value)),
            stoppingToken
        );

    /// <inheritdoc />
    public async Task<IList<UserEntity>> GetUsersForClaimAsync(
        Claim claim,
        CancellationToken stoppingToken
    ) => [.. await userRepository.ListByClaimAsync(claim.Type, claim.Value, stoppingToken)];

    // ------------------------------------------------------------------------------------------
    // IUserRoleStore<UserEntity>
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task AddToRoleAsync(
        UserEntity user,
        string normalizedRoleName,
        CancellationToken stoppingToken
    )
    {
        RoleEntity role = await roleRepository.FindByNormalizedNameAsync(
            normalizedRoleName,
            stoppingToken
        );

        // UserManager checks that the role exists before calling this, so a missing role
        // means that check was skipped.
        if (role is null)
            throw new InvalidOperationException(
                $"Cannot add user '{user.UserName}' to role '{normalizedRoleName}' because no role with that name exists."
            );

        await userRepository.AddToRoleAsync(user.Id, role.Id, stoppingToken);
    }

    /// <inheritdoc />
    public async Task RemoveFromRoleAsync(
        UserEntity user,
        string normalizedRoleName,
        CancellationToken stoppingToken
    )
    {
        RoleEntity role = await roleRepository.FindByNormalizedNameAsync(
            normalizedRoleName,
            stoppingToken
        );

        if (role is null)
            return;

        await userRepository.RemoveFromRoleAsync(user.Id, role.Id, stoppingToken);
    }

    /// <inheritdoc />
    public async Task<IList<string>> GetRolesAsync(UserEntity user, CancellationToken stoppingToken)
    {
        IReadOnlyList<RoleEntity> roles = await userRepository.ListRolesAsync(
            user.Id,
            stoppingToken
        );

        return [.. roles.Select(role => role.Name)];
    }

    /// <inheritdoc />
    public async Task<bool> IsInRoleAsync(
        UserEntity user,
        string normalizedRoleName,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<RoleEntity> roles = await userRepository.ListRolesAsync(
            user.Id,
            stoppingToken
        );

        return roles.Any(role => role.NormalizedName == normalizedRoleName);
    }

    /// <inheritdoc />
    public async Task<IList<UserEntity>> GetUsersInRoleAsync(
        string normalizedRoleName,
        CancellationToken stoppingToken
    ) => [.. await roleRepository.ListUsersInRoleAsync(normalizedRoleName, stoppingToken)];

    // ------------------------------------------------------------------------------------------
    // IUserAuthenticatorKeyStore<UserEntity>: the shared secret behind TOTP codes (RFC 6238),
    // encrypted at rest so a stolen database backup cannot produce valid codes.
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task SetAuthenticatorKeyAsync(
        UserEntity user,
        string key,
        CancellationToken stoppingToken
    )
    {
        user.TwoFactorTotpSecretEncrypted = key is null
            ? null
            : _authenticatorKeyProtector.Protect(key);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> GetAuthenticatorKeyAsync(
        UserEntity user,
        CancellationToken stoppingToken
    ) =>
        Task.FromResult(
            user.TwoFactorTotpSecretEncrypted is null
                ? null
                : _authenticatorKeyProtector.Unprotect(user.TwoFactorTotpSecretEncrypted)
        );

    // ------------------------------------------------------------------------------------------
    // IUserTwoFactorRecoveryCodeStore<UserEntity>: single-use codes a user can redeem instead
    // of an authenticator or e-mailed code when the usual second factor is unavailable (a lost
    // device, for example). Like the rest of the store, these only change the entity;
    // UserManager then calls UpdateAsync to save.
    // ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task ReplaceCodesAsync(
        UserEntity user,
        IEnumerable<string> recoveryCodes,
        CancellationToken stoppingToken
    )
    {
        string mergedCodes = string.Join(RecoveryCodeDelimiter, recoveryCodes);

        user.TwoFactorRecoveryCodesEncrypted =
            mergedCodes.Length == 0 ? null : _recoveryCodesProtector.Protect(mergedCodes);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RedeemCodeAsync(UserEntity user, string code, CancellationToken stoppingToken)
    {
        string[] remainingCodes = GetRecoveryCodes(user);

        // Codes are compared ordinally and case-sensitively, as the default Identity generator
        // produced them; entered text is not normalized.
        if (!remainingCodes.Contains(code, StringComparer.Ordinal))
            return Task.FromResult(false);

        string[] updatedCodes = [.. remainingCodes.Where(existing => existing != code)];
        string mergedCodes = string.Join(RecoveryCodeDelimiter, updatedCodes);

        user.TwoFactorRecoveryCodesEncrypted =
            mergedCodes.Length == 0 ? null : _recoveryCodesProtector.Protect(mergedCodes);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<int> CountCodesAsync(UserEntity user, CancellationToken stoppingToken) =>
        Task.FromResult(GetRecoveryCodes(user).Length);

    /// <summary>
    ///     This method decrypts and splits <see cref="UserEntity.TwoFactorRecoveryCodesEncrypted" />
    ///     into codes, or returns an empty array if no batch was generated or every code was used.
    /// </summary>
    /// <param name="user">The user whose recovery codes are read.</param>
    private string[] GetRecoveryCodes(UserEntity user)
    {
        if (string.IsNullOrEmpty(user.TwoFactorRecoveryCodesEncrypted))
            return [];

        string mergedCodes = _recoveryCodesProtector.Unprotect(
            user.TwoFactorRecoveryCodesEncrypted
        );

        return mergedCodes.Split(RecoveryCodeDelimiter, StringSplitOptions.RemoveEmptyEntries);
    }
}
