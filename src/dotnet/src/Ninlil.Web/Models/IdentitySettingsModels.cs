using Microsoft.AspNetCore.Identity;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the password complexity and account lockout policy that
///     <c>GET /api/identity-settings</c> returns: ASP.NET Core Identity's
///     <see cref="IdentityOptions.Password" /> and <see cref="IdentityOptions.Lockout" />. Nothing
///     in it is secret, so a <c>PATCH</c> to the same endpoint accepts the same fields.
///     <see cref="IdentityOptions.User" />, <see cref="IdentityOptions.SignIn" />,
///     <see cref="IdentityOptions.Tokens" />, <see cref="IdentityOptions.ClaimsIdentity" /> and
///     <see cref="IdentityOptions.Stores" /> are left out: they are structural choices the
///     identity wiring depends on (unique e-mail enforcement, for example, which rejects an
///     account with no e-mail), not policy to tune.
/// </summary>
/// <param name="RequiredLength">The fewest characters a new password may contain.</param>
/// <param name="RequiredUniqueChars">The fewest distinct characters a new password must contain.</param>
/// <param name="RequireDigit">Whether a new password must contain at least one digit.</param>
/// <param name="RequireLowercase">Whether a new password must contain at least one lowercase letter.</param>
/// <param name="RequireUppercase">Whether a new password must contain at least one uppercase letter.</param>
/// <param name="RequireNonAlphanumeric">Whether a new password must contain at least one non-alphanumeric character.</param>
/// <param name="DefaultLockoutTimeSpan">How long an account is locked out once <see cref="MaxFailedAccessAttempts" /> is reached.</param>
/// <param name="MaxFailedAccessAttempts">How many consecutive failed sign-in attempts trigger a lockout.</param>
/// <param name="AllowedForNewUsers">Whether lockout applies to a newly created account too.</param>
public record IdentitySettingsDetail(
    int RequiredLength,
    int RequiredUniqueChars,
    bool RequireDigit,
    bool RequireLowercase,
    bool RequireUppercase,
    bool RequireNonAlphanumeric,
    TimeSpan DefaultLockoutTimeSpan,
    int MaxFailedAccessAttempts,
    bool AllowedForNewUsers
);
