using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Identity.Store;

/// <summary>
///     This class is the bridge between ASP.NET Core Identity and Ninlil's own,
///     storage-agnostic <see cref="UserEntity" /> repository. ASP.NET Core Identity's
///     <see cref="UserManager{TUser}" /> never touches a database directly; every read or write
///     it needs goes through a store like this one, implementing whichever of Identity's small,
///     single-purpose store interfaces the feature in question requires. Because this class talks
///     to <see cref="IUserRepository" /> rather than to Entity Framework Core directly, the exact
///     same class works whether the application is configured to use a relational database or
///     MongoDB underneath.
///     <para>
///         Most of the methods below are simple property getters and setters on the in-memory
///         <see cref="UserEntity" /> instance Identity is already holding; Identity always
///         follows a mutating call with its own call to <see cref="UpdateAsync" />, which is
///         where this store actually commits anything to storage.
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
    ///     This constant contains the purpose string used to derive
    ///     <see cref="_authenticatorKeyProtector" />. Exposed publicly (rather than kept a private
    ///     literal) so that the cluster's own identity/RBAC federation mechanism
    ///     (<c>Ninlil.Dns.Server.ClusterSync</c>'s <c>UserClusterSyncEventSubscriber</c>/
    ///     <c>UserClusterConfigSyncApplier</c>) can derive the exact same protector from its own
    ///     node's own <see cref="IDataProtectionProvider" /> to decrypt-at-sender/re-encrypt-at-
    ///     receiver a TOTP authenticator key during a cluster sync, without duplicating this
    ///     magic string in a second place that could drift out of sync with this one.
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
    ///     This property contains the protector used to encrypt and decrypt a user's TOTP
    ///     authenticator key, so that the raw shared secret never sits in the database in plain
    ///     text.
    /// </summary>
    private readonly IDataProtector _authenticatorKeyProtector =
        dataProtectionProvider.CreateProtector(AuthenticatorKeyProtectorPurpose);

    /// <summary>
    ///     This property contains the protector used to encrypt and decrypt a user's own
    ///     two-factor recovery codes, so the plain-text codes never sit in the database. A
    ///     separate, dedicated purpose string from <see cref="_authenticatorKeyProtector" /> --
    ///     one protector per secret type, per this project's own established convention -- since
    ///     the two secrets protect genuinely independent things.
    /// </summary>
    private readonly IDataProtector _recoveryCodesProtector =
        dataProtectionProvider.CreateProtector(RecoveryCodesProtectorPurpose);

    /// <summary>
    ///     This constant is the delimiter <see cref="ReplaceCodesAsync" />/
    ///     <see cref="RedeemCodeAsync" /> join and split a user's own recovery codes on before
    ///     encrypting/after decrypting them -- the exact same delimiter ASP.NET Core Identity's
    ///     own default <c>UserStore</c> uses for this feature, chosen here purely for
    ///     familiarity/consistency with that convention, not because anything else depends on it.
    /// </summary>
    private const string RecoveryCodeDelimiter = ";";

    // ------------------------------------------------------------------------------------------
    // IUserStore<UserEntity> -- the small set of operations every Identity store must support:
    // reading and writing a user's identifier and name, and creating, updating, and deleting the
    // user record itself.
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
        // A freshly-created user needs its own identifier and creation timestamp.
        user.Id = user.Id == Guid.Empty ? Guid.NewGuid() : user.Id;
        user.CreatedAtUtc = DateTimeOffset.UtcNow;

        await userRepository.AddAsync(user, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // We're done, report success.
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> UpdateAsync(UserEntity user, CancellationToken stoppingToken)
    {
        // Record when this user was last modified.
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // This is the point where Identity always expects previously-staged changes -- a claim
        // added, a role granted, an authenticator key set -- to actually be committed.
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // We're done, report success.
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> DeleteAsync(UserEntity user, CancellationToken stoppingToken)
    {
        userRepository.Remove(user);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // We're done, report success.
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
        // Nothing here owns a disposable resource of its own; the repository, unit of work, and
        // data protector are all owned and disposed of by the dependency injection container.
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

        // ASP.NET Core Identity's RoleManager-backed UserManager always verifies a role exists
        // before asking the store to grant it, so reaching this point with no matching role
        // means something upstream skipped that check.
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

        // There is nothing to remove if the role does not exist in the first place.
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
    // IUserAuthenticatorKeyStore<UserEntity> -- the shared secret behind TOTP two-factor codes,
    // defined by RFC 6238. Encrypted at rest so that a stolen database backup alone is not enough
    // to mint valid authenticator codes for a user.
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
    // IUserTwoFactorRecoveryCodeStore<UserEntity> -- single-use fallback codes an operator can
    // redeem in place of an authenticator/e-mailed code when their normal second factor is
    // unavailable (a lost device, for example). Every method here follows the same in-memory-
    // mutation-then-UpdateAsync shape every other mutating member of this store already follows;
    // UserManager's own ReplaceCodesAsync/RedeemTwoFactorRecoveryCodeAsync callers always follow
    // a store mutation with their own call to UpdateAsync, which is what actually persists it.
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

        // A recovery code is compared with an ordinal, case-sensitive match -- exactly the codes
        // this project's own generator (Identity's default RecoveryCodeGenerator) produces them
        // in, with no normalization of operator-entered case, since none of the codes this
        // project generates are ever mixed-case-ambiguous by design (uppercase letters and
        // digits only).
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
    ///     back into its individual codes, or an empty array if the user has never generated a
    ///     batch (or has redeemed every one they were given).
    /// </summary>
    /// <param name="user">The user whose own recovery codes are being read.</param>
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
