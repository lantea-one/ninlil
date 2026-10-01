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
///     <see cref="RoleSummary.Permissions" />).
/// </param>
/// <param name="OriginatingNodeId">
///     The cluster node the account was last created or modified on, or <see langword="null" />
///     if it was never written with clustering enabled. The admin console shows it with
///     <see cref="LastModifiedUtc" /> in <c>ClusterSyncInfoComponent</c>, as for other synced
///     entities.
/// </param>
/// <param name="ClusterConflictNote">
///     An explanation of a cluster username collision this account lost, so an administrator can
///     see it was renamed or overwritten rather than silently dropped or merged (see
///     <c>UserEntity.ClusterConflictNote</c>). <see langword="null" /> when there was none
///     (almost always).
/// </param>
/// <param name="LastModifiedUtc">
///     When the account was last created or modified
///     (<c>AuditableEntityBaseExtensions.GetLastModifiedUtc()</c>).
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
///     and phone number. <see cref="Email" /> is shown, and lets the Profile page compute the
///     Gravatar hash, but cannot be changed here: changing an e-mail address would need a
///     re-verification flow that does not exist. An administrator can change it through
///     <c>UpdateUserRequest</c>.
/// </summary>
/// <param name="Id">The account's identifier.</param>
/// <param name="UserName">The account's sign-in name.</param>
/// <param name="Email">The account's e-mail address, if any. Read-only here.</param>
/// <param name="DisplayName">The account's friendly display name, if any.</param>
/// <param name="PhoneNumber">The account's phone number, if any.</param>
public record UserProfileDetail(
    Guid Id,
    string UserName,
    string Email,
    string DisplayName,
    string PhoneNumber
);

/// <summary>
///     This class carries the caller's self-service password change
///     (<see cref="Controllers.UsersController.ChangeMyPassword" />). <see cref="CurrentPassword" />
///     is required and checked by <c>UserManager&lt;TUser&gt;.ChangePasswordAsync</c>: unlike an
///     administrator's reset, a caller changing their own password must prove they know it.
/// </summary>
/// <param name="CurrentPassword">The account's current password.</param>
/// <param name="NewPassword">The password to change to.</param>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
///     This enumeration names the two ways an administrator's password reset (see
///     <see cref="Controllers.UsersController.ResetPassword" />) delivers a new credential to a
///     locked-out account. Both are needed: <see cref="AutoGenerate" /> works without any SMTP
///     server, which suits a homelab, while <see cref="EmailLink" /> lets the account holder pick
///     a password the administrator never sees, but needs a configured SMTP server (see
///     <c>EmailSenderOptions</c> and <c>SmtpSettingsController</c>).
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
///     The generated password, shown only here. Set only when <paramref name="Mode" /> is
///     <see cref="AdminPasswordResetMode.AutoGenerate" />; <see langword="null" /> for
///     <see cref="AdminPasswordResetMode.EmailLink" />, where the server never learns the new
///     password.
/// </param>
public record AdminResetPasswordResult(AdminPasswordResetMode Mode, string GeneratedPassword);
