using Microsoft.AspNetCore.Identity;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective password-
///     complexity and account-lockout policy -- <see cref="IdentityOptions.Password" /> and
///     <see cref="IdentityOptions.Lockout" />, ASP.NET Core Identity's own built-in policy classes,
///     which shipped inside this project's identity layer from the very start but, before this
///     controller existed, had zero admin-facing configuration surface: changing either one
///     required hand-editing the configuration file's own <c>Identity</c> section and restarting.
///     No secret material is involved, so this record's own fields are the exact same ones a
///     <c>PATCH</c> to the same endpoint accepts. Deliberately excludes
///     <see cref="IdentityOptions.User" />/<see cref="IdentityOptions.SignIn" />/
///     <see cref="IdentityOptions.Tokens" />/<see cref="IdentityOptions.ClaimsIdentity" />/
///     <see cref="IdentityOptions.Stores" /> -- structural choices this project's own identity
///     wiring depends on (unique e-mail enforcement, for one, which rejects an account with no e-mail), not
///     tunable policy in the sense this controller's own name promises.
/// </summary>
/// <param name="RequiredLength">The fewest characters a new password may contain.</param>
/// <param name="RequiredUniqueChars">The fewest distinct characters a new password must contain.</param>
/// <param name="RequireDigit">Whether a new password must contain at least one digit.</param>
/// <param name="RequireLowercase">Whether a new password must contain at least one lowercase letter.</param>
/// <param name="RequireUppercase">Whether a new password must contain at least one uppercase letter.</param>
/// <param name="RequireNonAlphanumeric">Whether a new password must contain at least one non-alphanumeric character.</param>
/// <param name="DefaultLockoutTimeSpan">How long an account is locked out once <see cref="MaxFailedAccessAttempts" /> is reached.</param>
/// <param name="MaxFailedAccessAttempts">How many consecutive failed sign-in attempts trigger a lockout.</param>
/// <param name="AllowedForNewUsers">Whether lockout applies to a brand-new account, not only an established one.</param>
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
