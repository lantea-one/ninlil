using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the settings for a newly-created user account.
/// </summary>
/// <param name="UserName">The account's sign-in name.</param>
/// <param name="Password">The account's initial password.</param>
/// <param name="Email">The account's e-mail address, if any.</param>
/// <param name="DisplayName">The account's friendly display name, if any.</param>
public record CreateUserRequest(string UserName, string Password, string Email, string DisplayName);

/// <summary>
///     This class carries the settings that can be changed on an existing user account.
///     Password changes are handled separately, not through this endpoint.
/// </summary>
/// <param name="DisplayName">The account's new friendly display name.</param>
/// <param name="Email">The account's new e-mail address.</param>
/// <param name="IsDisabled">Whether the account should be disabled (rejected at sign-in).</param>
public record UpdateUserRequest(string DisplayName, string Email, bool IsDisabled);

/// <summary>
///     This class carries a user account's own summary, for administrative listing purposes.
/// </summary>
/// <param name="Id">The account's own identifier.</param>
/// <param name="UserName">The account's sign-in name.</param>
/// <param name="Email">The account's e-mail address, if any.</param>
/// <param name="DisplayName">The account's friendly display name, if any.</param>
/// <param name="IsDisabled">Whether the account is currently disabled.</param>
/// <param name="TwoFactorEnabled">Whether the account requires a second authentication factor.</param>
/// <param name="Roles">The names of every role this account currently holds.</param>
/// <param name="DirectPermissions">
///     Every permission granted to this account directly, independent of any role it holds --
///     distinct from whatever a role it holds might separately grant, mirroring
///     <see cref="RoleSummary.Permissions" />'s own shape one level down.
/// </param>
/// <param name="OriginatingNodeId">
///     The cluster node this account was most recently created or modified on, or
///     <see langword="null" /> on a single-node deployment (or for an account that predates Phase
///     9-6d's own identity/RBAC cluster federation) -- mirrors <c>FilterListDetail</c>'s own field
///     of the same name and purpose, rendered the same way via the Angular admin SPA's shared
///     <c>ClusterSyncInfoComponent</c>.
/// </param>
/// <param name="ClusterConflictNote">
///     A human-readable explanation of a genuine cluster username collision this account lost,
///     surfaced so an administrator can see it was renamed/overwritten rather than silently
///     dropped or merged -- see <c>UserEntity.ClusterConflictNote</c>'s own doc comment.
///     <see langword="null" /> for the overwhelming majority of accounts, which never collided
///     with anything.
/// </param>
/// <param name="LastModifiedUtc">
///     The instant this account was last created or modified (<c>AuditableEntityBaseExtensions
///     .GetLastModifiedUtc()</c>) -- paired with <see cref="OriginatingNodeId" /> by the Angular
///     admin SPA's own shared <c>ClusterSyncInfoComponent</c>, mirroring every cluster-synced
///     entity summary's own field of the same name and purpose.
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
///     This class carries the role a user should be granted or revoked.
/// </summary>
/// <param name="RoleName">The name of the role to grant or revoke.</param>
public record UserRoleAssignmentRequest(string RoleName);

/// <summary>
///     This class carries a single permission claim to grant or revoke directly on a user,
///     independent of any role they hold.
/// </summary>
/// <param name="Permission">The permission string, such as <c>zones:write</c>.</param>
public record UserPermissionRequest(string Permission);

/// <summary>
///     This class carries the current caller's own self-service profile -- display name, phone
///     number, and e-mail address -- reachable via <see cref="Controllers.UsersController.GetMine" />
///     and <see cref="Controllers.UsersController.PatchMine" />. <see cref="Email" /> is included
///     purely for display and so the Angular admin SPA's own Profile page can compute this
///     account's Gravatar hash client-side; it is deliberately read-only through this pair of
///     actions (an e-mail change needs its own re-verification flow this project does not yet
///     build -- see <c>UpdateUserRequest</c>, the administrative equivalent, for the one path that
///     can still change it today).
/// </summary>
/// <param name="Id">The account's own identifier.</param>
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
///     This class carries the current caller's own self-service password change, reachable via
///     <see cref="Controllers.UsersController.ChangeMyPassword" />. <see cref="CurrentPassword" />
///     is required and checked for real (via <c>UserManager&lt;TUser&gt;.ChangePasswordAsync</c>)
///     -- unlike the administrator-driven reset below, a caller changing their own password must
///     prove they still know it.
/// </summary>
/// <param name="CurrentPassword">The account's current password.</param>
/// <param name="NewPassword">The password to change to.</param>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
///     This enumeration describes the two ways an administrator-driven password reset (see
///     <see cref="Controllers.UsersController.ResetPassword" />) can deliver the new credential to
///     a locked-out account. Confirmed via <c>AskUserQuestion</c> that both modes are needed, not
///     just one: <see cref="AutoGenerate" /> works with no SMTP server configured at all, fitting
///     this project's homelab-first deployment default, while <see cref="EmailLink" /> lets the
///     account holder pick their own new password without an administrator ever seeing it, at the
///     cost of requiring a working, already-configured SMTP server (see
///     <c>EmailSenderOptions</c>/<c>SmtpSettingsController</c>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AdminPasswordResetMode>))]
public enum AdminPasswordResetMode
{
    /// <summary>
    ///     A strong random password is generated server-side and set immediately, then returned
    ///     exactly once in the response body -- this project's usual reveal-once secret exposure
    ///     policy, mirroring <c>AdministratorBootstrapper</c>'s own bootstrap-password pattern.
    /// </summary>
    AutoGenerate,

    /// <summary>
    ///     A real ASP.NET Core Identity password-reset token is generated and e-mailed to the
    ///     account's own address as a link to this project's shared "set new password" landing
    ///     page (also used by the self-service Forgot Password flow) -- the password itself is
    ///     chosen by the account holder, never seen by the administrator who triggered the reset.
    /// </summary>
    EmailLink,
}

/// <summary>
///     This class carries which delivery mode an administrator-driven password reset (see
///     <see cref="Controllers.UsersController.ResetPassword" />) should use -- see
///     <see cref="AdminPasswordResetMode" />'s own doc comment for what each mode does.
/// </summary>
/// <param name="Mode">Which delivery mode to use.</param>
public record AdminResetPasswordRequest(AdminPasswordResetMode Mode);

/// <summary>
///     This class carries the outcome of an administrator-driven password reset.
/// </summary>
/// <param name="Mode">Which delivery mode was used.</param>
/// <param name="GeneratedPassword">
///     The newly-generated password, shown exactly this once -- set only when
///     <paramref name="Mode" /> is <see cref="AdminPasswordResetMode.AutoGenerate" />;
///     <see langword="null" /> for <see cref="AdminPasswordResetMode.EmailLink" />, since that mode
///     never learns the account holder's new password at all.
/// </param>
public record AdminResetPasswordResult(AdminPasswordResetMode Mode, string GeneratedPassword);
