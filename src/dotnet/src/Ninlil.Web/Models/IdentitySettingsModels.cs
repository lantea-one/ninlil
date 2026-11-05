using Microsoft.AspNetCore.Identity;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the password complexity policy that <c>GET /api/identity-settings</c>
///     returns, from ASP.NET Core Identity's <see cref="IdentityOptions.Password" />, plus three
///     deprecated lockout fields from <see cref="IdentityOptions.Lockout" /> that have no effect
///     and stay only so existing callers keep working. Swashbuckle marks them
///     <c>deprecated</c> in the OpenAPI document because of their
///     <see cref="ObsoleteAttribute" />. Nothing in it is secret, so a <c>PATCH</c> to the same endpoint accepts the same fields.
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
/// <param name="DefaultLockoutTimeSpan">
///     Deprecated, and it has no effect: Ninlil has no account lockout. Sign-in is rate limited
///     per client address by <c>AdminLoginRateLimitOptions</c> instead. A later release removes
///     this field.
/// </param>
/// <param name="MaxFailedAccessAttempts">
///     Deprecated, and it has no effect: Ninlil has no account lockout. Sign-in is rate limited
///     per client address by <c>AdminLoginRateLimitOptions</c> instead. A later release removes
///     this field.
/// </param>
/// <param name="AllowedForNewUsers">
///     Deprecated, and it has no effect: Ninlil has no account lockout. Sign-in is rate limited
///     per client address by <c>AdminLoginRateLimitOptions</c> instead. A later release removes
///     this field.
/// </param>
/// <param name="RequireEmailChangeConfirmation">
///     Whether a user who changes their own e-mail address must first confirm the new one through
///     an e-mailed link (<c>Identity:EmailChange:RequireConfirmation</c>).
/// </param>
public record IdentitySettingsDetail(
    int RequiredLength,
    int RequiredUniqueChars,
    bool RequireDigit,
    bool RequireLowercase,
    bool RequireUppercase,
    bool RequireNonAlphanumeric,
    [property: Obsolete("Has no effect. A later release removes this field.")]
        TimeSpan DefaultLockoutTimeSpan,
    [property: Obsolete("Has no effect. A later release removes this field.")]
        int MaxFailedAccessAttempts,
    [property: Obsolete("Has no effect. A later release removes this field.")]
        bool AllowedForNewUsers,
    bool RequireEmailChangeConfirmation
);
