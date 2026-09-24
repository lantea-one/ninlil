using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Identity.Sso;

/// <summary>
///     This class implements <see cref="IOidcUserProvisioningService" />: it matches an OpenID
///     Connect sign-in against a local account by the identity provider's own immutable
///     <c>sub</c> claim -- never by e-mail address, since an e-mail address is something a
///     provider can legally reuse or reassign to a different person over time, while a subject
///     identifier is not -- and, on that subject's first-ever sign-in, provisions a brand-new
///     local account for it.
///     <para>
///         <b>Auto-provisioning is deliberately conservative: a freshly-provisioned account gets
///         no roles and no permission claims at all</b>, exactly as if an administrator had used
///         <c>Web.Controllers.UsersController.Create</c> to add a brand-new local account and had
///         not yet granted it anything. An operator must explicitly grant roles/permissions to a
///         newly-federated account afterward, through the same administrative surface as any
///         other account, before it can do anything beyond simply being signed in. This is a
///         considered choice, not an oversight -- silently trusting an identity provider's own
///         group/role claims (which this service does not even read) to decide administrative
///         capability on this server would let a misconfigured or compromised upstream IdP grant
///         itself privileges here with zero local review.
///     </para>
///     <para>
///         Persistence goes directly through <see cref="IUserRepository" />/<see cref="IUnitOfWork" />,
///         the same repository-plus-unit-of-work shape <c>Token.JwtTokenService.CreateRefreshTokenAsync</c>
///         already establishes, rather than through <c>Microsoft.AspNetCore.Identity.UserManager{TUser}</c>
///         -- there is no password to hash or validate here, and this service needs to set
///         <see cref="UserEntity.ExternalIdentityProvider" />/<see cref="UserEntity.ExternalSubjectId" />,
///         fields <c>UserManager{TUser}</c> has no concept of.
///     </para>
/// </summary>
/// <param name="userRepository">The repository this service reads accounts from and writes newly-provisioned accounts to.</param>
/// <param name="unitOfWork">The unit of work a newly-provisioned account is committed through.</param>
public sealed class OidcUserProvisioningService(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork
) : IOidcUserProvisioningService
{
    /// <summary>
    ///     This constant contains the fixed value every OIDC-provisioned account's own
    ///     <see cref="UserEntity.ExternalIdentityProvider" /> is set to -- this project supports
    ///     exactly one external identity provider kind today.
    /// </summary>
    public const string ProviderName = "Oidc";

    /// <summary>
    ///     This constant contains the domain suffix a synthesized placeholder e-mail address is
    ///     built from when the identity provider's own token carries no <c>email</c> claim at all
    ///     -- an address deliberately structured so it can never resolve or accept mail, since
    ///     <c>RequireUniqueEmail=true</c> rejects a <see langword="null" />/empty e-mail as
    ///     genuinely invalid, not merely absent (see the codebase's own bug catalog, entry 15),
    ///     and this project's own <see cref="UserEntity.Email" /> column is otherwise always
    ///     expected to hold something.
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

        // An already-provisioned account is matched by subject alone -- no field on it is ever
        // synced from a later sign-in's own claims, deliberately: an operator may have since
        // renamed the account, corrected its e-mail address, or otherwise diverged it from what
        // the identity provider currently reports, and a silent overwrite on every sign-in would
        // undo that every time.
        UserEntity existingUser = await userRepository.FindByExternalSubjectAsync(
            ProviderName,
            subjectId,
            stoppingToken
        );

        if (existingUser is not null)
            return existingUser;

        // This is this subject's first sign-in -- provision a brand-new, permission-less account
        // for it.
        string preferredUsername = ReadClaim(principal, "preferred_username");
        string candidateUserName = string.IsNullOrWhiteSpace(preferredUsername)
            ? subjectId
            : preferredUsername;
        string userName = await ResolveUniqueUserNameAsync(candidateUserName, stoppingToken);

        string name = ReadClaim(principal, "name");
        string realEmail = ReadClaim(principal, JwtRegisteredClaimNames.Email, ClaimTypes.Email);
        bool hasRealEmail = !string.IsNullOrWhiteSpace(realEmail);
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

            // The identity provider already vouched for the address by including it in a signed
            // token -- confirmed only when it actually sent one; a synthesized placeholder is
            // never treated as confirmed.
            EmailConfirmed = hasRealEmail,

            // This account can only ever sign in through this OIDC flow -- a null password hash
            // means IUserPasswordStore.HasPasswordAsync reports "no password", and Identity's own
            // password sign-in path rejects it outright rather than trying to verify a null hash.
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
    ///     This method resolves a candidate user name against every existing local account,
    ///     appending an incrementing numeric suffix (<c>-2</c>, <c>-3</c>, and so on) until it
    ///     finds one that is not already taken -- the same collision-handling shape an
    ///     administrator hits (via <c>Microsoft.AspNetCore.Identity.UserManager{TUser}.CreateAsync</c>'s
    ///     own duplicate-name validation) when two accounts would otherwise share a sign-in name,
    ///     applied here proactively instead of reactively since this service bypasses
    ///     <c>UserManager{TUser}</c> entirely (see this class's own doc comment).
    /// </summary>
    /// <param name="candidateUserName">The identity provider's own preferred user name, or the subject identifier if it supplied none.</param>
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
    ///     This method reads the first present claim, by type, off a principal -- tried in the
    ///     order given, since <c>OpenIdConnectOptions.MapInboundClaims</c> is explicitly disabled
    ///     for this scheme (see <c>ServiceCollectionAuthenticationExtension</c>'s own doc comment,
    ///     and the codebase's own bug catalog entry 16 on why this matters), so a real OIDC claim
    ///     always arrives under its own standard short name (<c>sub</c>, <c>email</c>, and so on)
    ///     rather than a renamed <see cref="ClaimTypes" /> equivalent -- the
    ///     <see cref="ClaimTypes" /> fallback exists only in case a non-standard provider or a
    ///     test double supplies the claim that way instead.
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
