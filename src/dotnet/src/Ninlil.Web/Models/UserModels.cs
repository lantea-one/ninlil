using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the settings for a new user account (<c>POST /api/users</c>).
/// </summary>
/// <param name="UserName">The account's sign-in name.</param>
/// <param name="Password">The account's initial password.</param>
/// <param name="Email">The account's e-mail address, if any.</param>
/// <param name="DisplayName">The account's friendly display name, if any.</param>
public record CreateUserRequest(string UserName, string Password, string Email, string DisplayName);

/// <summary>
///     This class carries the settings an administrator can change on a user account
///     (<c>PUT /api/users/{id}</c>). Passwords are changed through separate endpoints.
/// </summary>
/// <param name="DisplayName">The account's new friendly display name.</param>
/// <param name="Email">The account's new e-mail address.</param>
/// <param name="IsDisabled">Whether the account should be disabled (rejected at sign-in).</param>
public record UpdateUserRequest(string DisplayName, string Email, bool IsDisabled);

/// <summary>
///     This class carries a user account, as returned by the administrative endpoints of
///     <c>/api/users</c>.
/// </summary>
/// <param name="Id">The account's identifier.</param>
/// <param name="UserName">The account's sign-in name.</param>
/// <param name="Email">The account's e-mail address, if any.</param>
/// <param name="DisplayName">The account's friendly display name, if any.</param>
/// <param name="IsDisabled">Whether the account is disabled.</param>
/// <param name="TwoFactorEnabled">Whether the account requires a second authentication factor.</param>
/// <param name="Roles">The names of every role the account holds.</param>
/// <param name="DirectPermissions">
///     The permissions granted to the account directly, not through a role (the counterpart of
///     <c>RoleSummary.permissions</c>).
/// </param>
/// <param name="OriginatingNodeId">
///     The cluster node the account was last created or modified on, or <see langword="null" />
///     if it was never written with clustering enabled. The admin console shows it with
///     <c>lastModifiedUtc</c>, as for other synced entities.
/// </param>
/// <param name="ClusterConflictNote">
///     An explanation of a cluster username collision this account lost, so an administrator can
///     see it was renamed or overwritten rather than silently dropped or merged.
///     <see langword="null" /> when there was none (almost always).
/// </param>
/// <param name="LastModifiedUtc">
///     When the account was last created or modified
///     (the later of its creation and update times).
/// </param>
public record UserSummary(
    Guid Id,
    string UserName,
    string Email,
    string DisplayName,
    bool IsDisabled,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> DirectPermissions,
    Guid? OriginatingNodeId = null,
    string ClusterConflictNote = null,
    DateTimeOffset? LastModifiedUtc = null
);

/// <summary>
///     This class carries the role to grant a user (<c>POST /api/users/{id}/roles</c>).
/// </summary>
/// <param name="RoleName">The name of the role to grant or revoke.</param>
public record UserRoleAssignmentRequest(string RoleName);

/// <summary>
///     This class carries one permission to grant or revoke directly on a user, outside any role
///     (<c>POST</c> or <c>DELETE /api/users/{id}/permissions</c>).
/// </summary>
/// <param name="Permission">The permission string, such as <c>zones:write</c>.</param>
public record UserPermissionRequest(string Permission);

/// <summary>
///     This class carries the caller's self-service profile, read through
///     <see cref="Controllers.UsersController.GetMine" /> and changed through
///     <see cref="Controllers.UsersController.PatchMine" />, which accepts only the display name
///     and phone number. <c>email</c> is shown, and lets the Profile page compute the
///     Gravatar hash, but <see cref="Controllers.UsersController.PatchMine" /> ignores it: the
///     address changes only through <see cref="Controllers.UsersController.ChangeMyEmail" />,
///     which needs the current password. An administrator can change it through
///     <c>UpdateUserRequest</c>.
/// </summary>
/// <param name="Id">The account's identifier.</param>
/// <param name="UserName">The account's sign-in name.</param>
/// <param name="Email">The account's e-mail address, if any. Read-only here.</param>
/// <param name="DisplayName">The account's friendly display name, if any.</param>
/// <param name="PhoneNumber">The account's phone number, if any.</param>
/// <param name="EmailChangeRequiresConfirmation">
///     Whether <see cref="Controllers.UsersController.ChangeMyEmail" /> sends a confirmation link
///     to the new address first (<see langword="true" />) or changes the address at once. The
///     Profile page uses it to word its form. It mirrors <c>Identity:EmailChange:RequireConfirmation</c>.
/// </param>
public record UserProfileDetail(
    Guid Id,
    string UserName,
    string Email,
    string DisplayName,
    string PhoneNumber,
    bool EmailChangeRequiresConfirmation
);

/// <summary>
///     This class carries the caller's self-service password change
///     (<see cref="Controllers.UsersController.ChangeMyPassword" />). <c>currentPassword</c>
///     is required and checked by ASP.NET Core Identity: unlike an
///     administrator's reset, a caller changing their own password must prove they know it.
/// </summary>
/// <param name="CurrentPassword">The account's current password.</param>
/// <param name="NewPassword">The password to change to.</param>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
///     This class carries the caller's self-service e-mail address change
///     (<see cref="Controllers.UsersController.ChangeMyEmail" />). The current password is
///     required, as for a password change, because the address receives password-reset links and
///     e-mailed two-factor codes: whoever holds a stolen access token must not be able to redirect
///     them.
/// </summary>
/// <param name="NewEmail">The address to change to. It must be valid and not used by another account.</param>
/// <param name="CurrentPassword">The account's current password.</param>
public record ChangeEmailRequest(string NewEmail, string CurrentPassword);

/// <summary>
///     This class is the 202 answer of <see cref="Controllers.UsersController.ChangeMyEmail" />
///     when the new address must be confirmed first: a link has been sent and nothing on the
///     account has changed yet.
/// </summary>
/// <param name="ConfirmationSent">Always <see langword="true" />.</param>
public record ChangeEmailPendingResult(bool ConfirmationSent);

/// <summary>
///     This enumeration names the two ways an administrator's password reset (see
///     <see cref="Controllers.UsersController.ResetPassword" />) delivers a new credential to a
///     locked-out account. Both are needed: <c>AutoGenerate</c> works without any SMTP
///     server, which suits a homelab, while <c>EmailLink</c> lets the account holder pick
///     a password the administrator never sees, but needs a configured SMTP server (see
///     <c>GET /api/smtp-settings</c>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AdminPasswordResetMode>))]
public enum AdminPasswordResetMode
{
    /// <summary>
    ///     A strong random password is generated on the server, set at once and returned once in
    ///     the response, as secrets always are and as <c>AdministratorBootstrapper</c> does with
    ///     the bootstrap password.
    /// </summary>
    AutoGenerate,

    /// <summary>
    ///     An ASP.NET Core Identity password reset token is e-mailed to the account's address as a
    ///     link to the "set new password" page, which the self-service Forgot Password flow also
    ///     uses. The account holder chooses the password; the administrator never sees it.
    /// </summary>
    EmailLink,
}

/// <summary>
///     This class carries the delivery mode for an administrator's password reset (see
///     <see cref="Controllers.UsersController.ResetPassword" /> and
///     <see cref="AdminPasswordResetMode" />).
/// </summary>
/// <param name="Mode">Which delivery mode to use.</param>
public record AdminResetPasswordRequest(AdminPasswordResetMode Mode);

/// <summary>
///     This class carries the outcome of an administrator's password reset.
/// </summary>
/// <param name="Mode">Which delivery mode was used.</param>
/// <param name="GeneratedPassword">
///     The generated password, shown only here. Set only when <c>mode</c> is
///     <c>AutoGenerate</c>; <see langword="null" /> for
///     <c>EmailLink</c>, where the server never learns the new
///     password.
/// </param>
public record AdminResetPasswordResult(AdminPasswordResetMode Mode, string GeneratedPassword);
