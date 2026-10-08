using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Identity.Sso;

/// <summary>
///     This class implements <see cref="IOidcUserProvisioningService" />. It matches an OpenID
///     Connect sign-in to a local account by the provider's immutable <c>sub</c> claim, never by
///     e-mail address, which a provider can reassign to another person. On a subject's first
///     sign-in it creates a local account.
///     <para>
///         <b>A new account gets no roles and no permission claims</b>, like a local account just
///         created with <c>Web.Controllers.UsersController.Create</c>. An operator must grant
///         roles or permissions before it can do more than sign in. The provider's group or role
///         claims are not read, so a misconfigured or compromised provider cannot grant itself
///         privileges here.
///     </para>
///     <para>
///         <b>Refusals.</b> A linked account that is disabled or locked out is refused, as a
///         password sign-in refuses it, and so is a new account whose verified e-mail address
///         already belongs to another account. Both throw <see cref="OidcSignInRefusedException" />.
///         A new account takes the provider's e-mail address only when the provider asserts
///         <c>email_verified=true</c>; otherwise it gets a placeholder address. A taken user name
///         gets a numeric suffix.
///     </para>
///     <para>
///         It writes through <see cref="IUserRepository" /> and <see cref="IUnitOfWork" /> rather
///         than <c>Microsoft.AspNetCore.Identity.UserManager{TUser}</c>, because there is no
///         password to handle and it must set <see cref="UserEntity.ExternalIdentityProvider" />
///         and <see cref="UserEntity.ExternalSubjectId" />, which <c>UserManager{TUser}</c> does not
///         know about.
///     </para>
/// </summary>
/// <param name="userRepository">The repository accounts are read from and new accounts written to.</param>
/// <param name="unitOfWork">The unit of work a new account is saved through.</param>
public sealed class OidcUserProvisioningService(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork
) : IOidcUserProvisioningService
{
    /// <summary>
    ///     This constant contains the <see cref="UserEntity.ExternalIdentityProvider" /> value of every
    ///     OIDC-provisioned account; OIDC is the only external provider kind.
    /// </summary>
    public const string ProviderName = "Oidc";

    /// <summary>
    ///     This constant contains the domain of the placeholder e-mail address used when the token
    ///     has no <c>email</c> claim. The <c>.invalid</c> TLD never resolves or accepts mail. A
    ///     placeholder is needed because, with <c>RequireUniqueEmail=true</c>, Identity's user
    ///     validation (run by <c>UserManager</c> updates) rejects a null or empty e-mail.
    /// </summary>
    private const string PlaceholderEmailDomain = "sso.invalid";

    /// <inheritdoc />
    public async Task<UserEntity> FindOrProvisionUserAsync(
        ClaimsPrincipal principal,
        CancellationToken stoppingToken = default
    )
    {
        string subjectId = ReadClaim(
            principal,
            JwtRegisteredClaimNames.Sub,
            ClaimTypes.NameIdentifier
        );

        if (string.IsNullOrWhiteSpace(subjectId))
            throw new InvalidOperationException(
                "The OpenID Connect identity provider's own token carried no 'sub' claim -- this "
                    + "server cannot identify who signed in without one."
            );

        // An existing account is matched by subject only and never updated from later claims,
        // so an operator's renames or e-mail corrections are not overwritten on each sign-in.
        UserEntity existingUser = await userRepository.FindByExternalSubjectAsync(
            ProviderName,
            subjectId,
            stoppingToken
        );

        if (existingUser is not null)
        {
            // A provider login must not bypass what a password login enforces.
            if (existingUser.IsDisabled)
                throw new OidcSignInRefusedException(
                    $"The account '{existingUser.UserName}' is disabled."
                );

            if (
                existingUser is { LockoutEnabled: true, LockoutEndUtc: { } lockoutEndUtc }
                && lockoutEndUtc > DateTimeOffset.UtcNow
            )
                throw new OidcSignInRefusedException(
                    $"The account '{existingUser.UserName}' is locked out until {lockoutEndUtc:O}."
                );

            return existingUser;
        }

        // First sign-in: create an account with no permissions.
        string preferredUsername = ReadClaim(principal, "preferred_username");
        string candidateUserName = string.IsNullOrWhiteSpace(preferredUsername)
            ? subjectId
            : preferredUsername;
        string userName = await ResolveUniqueUserNameAsync(candidateUserName, stoppingToken);

        string name = ReadClaim(principal, "name");
        string claimedEmail = ReadClaim(principal, JwtRegisteredClaimNames.Email, ClaimTypes.Email);

        // Only an address the provider vouches for (email_verified=true) is stored. An unverified
        // address could be one the user does not own, so storing it would let them squat on it
        // and block its real owner; such an account gets the placeholder instead.
        bool hasRealEmail = !string.IsNullOrWhiteSpace(claimedEmail) && IsEmailVerified(principal);
        string realEmail = hasRealEmail ? claimedEmail : null;

        // A verified address that already belongs to another account is refused, not merged:
        // linking by e-mail is how a provider-side address change or reassignment becomes an
        // account takeover, and Identity's RequireUniqueEmail would break the duplicate later.
        if (
            hasRealEmail
            && await userRepository.FindByNormalizedEmailAsync(
                realEmail.ToUpperInvariant(),
                stoppingToken
            )
                is not null
        )
            throw new OidcSignInRefusedException(
                $"The e-mail address '{realEmail}' already belongs to another local account."
            );

        string effectiveEmail = hasRealEmail ? realEmail : $"{subjectId}@{PlaceholderEmailDomain}";

        UserEntity newUser = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            DisplayName = string.IsNullOrWhiteSpace(name) ? userName : name,
            Email = effectiveEmail,
            NormalizedEmail = effectiveEmail.ToUpperInvariant(),

            // An address from the signed token counts as confirmed; a placeholder never does.
            EmailConfirmed = hasRealEmail,

            // The account signs in only through OIDC: with a null hash, HasPasswordAsync reports no
            // password and password sign-in rejects the account.
            PasswordHash = null,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            ExternalIdentityProvider = ProviderName,
            ExternalSubjectId = subjectId,
        };

        await userRepository.AddAsync(newUser, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return newUser;
    }

    /// <summary>
    ///     This method makes a candidate user name unique by appending <c>-2</c>, <c>-3</c>, and so
    ///     on until no account has it. <c>UserManager{TUser}</c> would reject a duplicate name, but
    ///     this service does not go through it, so it avoids the clash up front.
    /// </summary>
    /// <param name="candidateUserName">The provider's preferred user name, or the subject identifier if it sent none.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    private async Task<string> ResolveUniqueUserNameAsync(
        string candidateUserName,
        CancellationToken stoppingToken
    )
    {
        string attempt = candidateUserName;
        int suffix = 2;

        while (
            await userRepository.FindByNormalizedUserNameAsync(
                attempt.ToUpperInvariant(),
                stoppingToken
            )
                is not null
        )
        {
            attempt = $"{candidateUserName}-{suffix}";
            suffix++;
        }

        return attempt;
    }

    /// <summary>
    ///     This method reports whether the provider asserts the token's e-mail address is verified.
    ///     Providers send <c>email_verified</c> as a JSON boolean or, in some, as the string
    ///     <c>"true"</c>; a missing or unreadable claim counts as not verified.
    /// </summary>
    /// <param name="principal">The principal to read the claim from.</param>
    private static bool IsEmailVerified(ClaimsPrincipal principal) =>
        bool.TryParse(ReadClaim(principal, "email_verified"), out bool verified) && verified;

    /// <summary>
    ///     This method returns the first claim present among the given types, in order.
    ///     <c>OpenIdConnectOptions.MapInboundClaims</c> is disabled for this scheme (see
    ///     <c>ServiceCollectionAuthenticationExtension</c>), so OIDC claims arrive under their short
    ///     names (<c>sub</c>, <c>email</c>, and so on). The <see cref="ClaimTypes" /> fallbacks cover a
    ///     non-standard provider or a test double.
    /// </summary>
    /// <param name="principal">The principal to read a claim from.</param>
    /// <param name="claimTypes">Every claim type to try, in priority order.</param>
    private static string ReadClaim(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (string claimType in claimTypes)
        {
            string value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
