using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Token;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Examples.Users;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Ninlil.Web.Services;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages user accounts: creating, updating and deleting them, and granting
///     or revoking the roles and direct permissions that decide what an account can do. The
///     administrative actions need <c>users:read</c> or <c>users:write</c>.
///     <para>
///         <b>The self-service actions need only a signed-in caller.</b> <see cref="GetMine" />
///         and <see cref="PatchMine" /> serve the admin console's Profile page and expose only
///         <see cref="UserEntity.DisplayName" />, <see cref="UserEntity.PhoneNumber" /> and a
///         read-only <see cref="UserEntity.Email" />, never roles, permissions or lockout state.
///         <see cref="ChangeMyPassword" /> also requires the current password, because a bearer
///         token proves who is signed in now, not that the caller knows the account's password.
///         <see cref="ResetPassword" /> is the administrator's counterpart for an account that
///         cannot supply its current password, and needs <c>users:write</c>.
///     </para>
/// </summary>
/// <param name="userManager">Creates, updates and deletes user accounts.</param>
/// <param name="roleManager">
///     Reads a role's permissions, so <see cref="AddToRole" /> can refuse a role that carries
///     permissions the caller does not hold.
/// </param>
/// <param name="jwtTokenService">
///     Revokes the account's refresh tokens when its password changes (see
///     <see cref="ChangeMyPassword" /> and <see cref="ResetPassword" />).
/// </param>
/// <param name="userRepository">
///     Lists user accounts; <see cref="UserManager{TUser}.Users" /> is unavailable because
///     <c>NinlilDnsUserStore</c> does not implement <c>IQueryableUserStore</c>.
/// </param>
/// <param name="emailOptions">
///     The current SMTP settings, read on every reset request so the "is SMTP configured" check
///     reflects the latest <c>SmtpSettingsController.Patch</c>.
/// </param>
/// <param name="passwordResetLinkSender">
///     Builds and sends the reset-link e-mail. <c>AuthController.ForgotPassword</c> uses it too,
///     so both flows send the same message.
/// </param>
/// <param name="eventPublisher">
///     Publishes a <see cref="UserAccountChangedEvent" /> on every change, which
///     <c>UserClusterSyncEventSubscriber</c> in <c>Ninlil.Dns.Server.ClusterSync</c> replicates
///     to the other cluster nodes.
/// </param>
/// <param name="usernameUniquenessChecker">
///     The best-effort, cluster-wide username check that <see cref="Create" /> consults (see
///     <see cref="IClusterUsernameUniquenessChecker" />).
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, stamped onto
///     <see cref="AuditableEntityBase.OriginatingNodeId" /> on every local change so the account
///     shows which node changed it last and cluster sync can break a last-write-wins tie.
/// </param>
/// <param name="identityOptions">
///     Supplies the live password policy an auto-generated password has to meet.
/// </param>
[ApiController]
[Route("api/users")]
public class UsersController(
    UserManager<UserEntity> userManager,
    RoleManager<RoleEntity> roleManager,
    IJwtTokenService jwtTokenService,
    IUserRepository userRepository,
    IOptionsMonitor<EmailSenderOptions> emailOptions,
    PasswordResetLinkSender passwordResetLinkSender,
    IDnsServerEventPublisher eventPublisher,
    IClusterUsernameUniquenessChecker usernameUniquenessChecker,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    IOptionsMonitor<IdentityOptions> identityOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists user accounts, one page at a time, with their roles and direct
    ///     permissions. Requires <c>users:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("users:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(UserSummaryPagedResultExample))]
    public async Task<ActionResult<PagedResult<UserSummary>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<UserEntity> users = await userRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await userRepository.CountAsync(stoppingToken);

        List<UserSummary> summaries = [];
        foreach (UserEntity user in users)
            summaries.Add(await ToSummaryAsync(user));

        return Ok(new PagedResult<UserSummary>(summaries, skip, take, totalCount));
    }

    /// <summary>
    ///     This action returns one user account. Returns 404 for an unknown id. Requires
    ///     <c>users:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("users:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(UserSummaryExample))]
    public async Task<ActionResult<UserSummary>> Get(Guid id)
    {
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            throw new NotFoundException("User", id.ToString());

        return Ok(await ToSummaryAsync(user));
    }

    /// <summary>
    ///     This action creates a user account. It first asks every reachable, healthy cluster peer
    ///     whether the username exists there and returns 409 if one does; an unreachable peer is
    ///     skipped, so user management never blocks on one node being down. A collision that only
    ///     shows up later (after a network partition) is resolved when the nodes resync, by the
    ///     last-write-wins handling in <c>UserClusterConfigSyncApplier</c>. Returns 400 when the
    ///     username, e-mail address or password fails validation. Requires <c>users:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("users:write")]
    [SwaggerRequestExample(typeof(CreateUserRequest), typeof(CreateUserRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(UserSummaryExample))]
    public async Task<ActionResult<UserSummary>> Create(
        CreateUserRequest request,
        CancellationToken stoppingToken
    )
    {
        string normalizedUserName = request.UserName?.ToUpperInvariant();

        if (
            !string.IsNullOrEmpty(normalizedUserName)
            && await usernameUniquenessChecker.ExistsOnAnyHealthyPeerAsync(
                normalizedUserName,
                stoppingToken
            )
        )
            throw new ConflictException(
                $"The username '{request.UserName}' already exists on another cluster node."
            );

        UserEntity user = new()
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName,
            Email = request.Email,
            DisplayName = request.DisplayName,
            OriginatingNodeId = clusterOptions.CurrentValue.NodeId,
        };

        IdentityResult result = await userManager.CreateAsync(user, request.Password);
        result.ThrowIfFailed();

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return CreatedAtAction(nameof(Get), new { id = user.Id }, await ToSummaryAsync(user));
    }

    /// <summary>
    ///     This action updates an account's display name, e-mail address and disabled flag. A
    ///     disabled account cannot sign in or refresh its session. Passwords are changed through
    ///     <see cref="ResetPassword" /> or <see cref="ChangeMyPassword" />. Requires
    ///     <c>users:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("users:write")]
    [SwaggerRequestExample(typeof(UpdateUserRequest), typeof(UpdateUserRequestExample))]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request)
    {
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            throw new NotFoundException("User", id.ToString());

        user.DisplayName = request.DisplayName;
        user.Email = request.Email;
        user.IsDisabled = request.IsDisabled;
        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;

        IdentityResult result = await userManager.UpdateAsync(user);
        result.ThrowIfFailed();

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action deletes a user account. Requires <c>users:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("users:write")]
    public async Task<IActionResult> Delete(Guid id)
    {
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            throw new NotFoundException("User", id.ToString());

        await userManager.DeleteAsync(user);

        eventPublisher.Publish(
            new UserAccountChangedEvent(DateTimeOffset.UtcNow, id, IsDeleted: true)
        );

        return NoContent();
    }

    /// <summary>
    ///     This action adds a user to a role, by role name. Returns 400 when the role does not
    ///     exist or the user already holds it. Requires <c>users:write</c>, and the caller must
    ///     hold every permission the role carries (a role holding <c>*</c> needs a <c>*</c>
    ///     holder); otherwise this returns 403 naming the missing permissions.
    /// </summary>
    [HttpPost("{id:guid}/roles")]
    [RequirePermission("users:write")]
    [SwaggerRequestExample(
        typeof(UserRoleAssignmentRequest),
        typeof(UserRoleAssignmentRequestExample)
    )]
    public async Task<IActionResult> AddToRole(Guid id, UserRoleAssignmentRequest request)
    {
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            throw new NotFoundException("User", id.ToString());

        RoleEntity role = await roleManager.FindByNameAsync(request.RoleName);
        if (role is not null)
        {
            IList<Claim> roleClaims = await roleManager.GetClaimsAsync(role);
            ObjectResult refusal = this.RefuseUnheldPermissions(
                roleClaims
                    .Where(claim => claim.Type == NinlilDnsClaimTypes.Permission)
                    .Select(claim => claim.Value)
            );
            if (refusal is not null)
                return refusal;
        }

        IdentityResult result = await userManager.AddToRoleAsync(user, request.RoleName);
        result.ThrowIfFailed();

        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await userManager.UpdateAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action removes a user from a role. Requires <c>users:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}/roles/{roleName}")]
    [RequirePermission("users:write")]
    public async Task<IActionResult> RemoveFromRole(Guid id, string roleName)
    {
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            throw new NotFoundException("User", id.ToString());

        await userManager.RemoveFromRoleAsync(user, roleName);

        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await userManager.UpdateAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action grants a user a permission directly, independent of their roles. The
    ///     permission string is not checked against <c>GET /api/permissions</c>. Requires
    ///     <c>users:write</c>, and the caller must hold the permission it grants (only a holder of
    ///     <c>*</c> can grant <c>*</c>); otherwise this returns 403 naming the missing permission.
    /// </summary>
    [HttpPost("{id:guid}/permissions")]
    [RequirePermission("users:write")]
    [SwaggerRequestExample(typeof(UserPermissionRequest), typeof(UserPermissionRequestExample))]
    public async Task<IActionResult> AddPermission(Guid id, UserPermissionRequest request)
    {
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            throw new NotFoundException("User", id.ToString());

        ObjectResult refusal = this.RefuseUnheldPermissions([request.Permission]);
        if (refusal is not null)
            return refusal;

        await userManager.AddClaimAsync(
            user,
            new Claim(NinlilDnsClaimTypes.Permission, request.Permission)
        );

        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await userManager.UpdateAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action revokes a permission granted to a user directly. Permissions that come from
    ///     the user's roles are unaffected. Requires <c>users:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}/permissions")]
    [RequirePermission("users:write")]
    [SwaggerRequestExample(typeof(UserPermissionRequest), typeof(UserPermissionRequestExample))]
    public async Task<IActionResult> RemovePermission(Guid id, UserPermissionRequest request)
    {
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            throw new NotFoundException("User", id.ToString());

        await userManager.RemoveClaimAsync(
            user,
            new Claim(NinlilDnsClaimTypes.Permission, request.Permission)
        );

        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await userManager.UpdateAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action returns the caller's profile: display name, phone number and read-only
    ///     e-mail address. Requires only a signed-in caller.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(UserProfileDetailExample))]
    public async Task<ActionResult<UserProfileDetail>> GetMine()
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        return Ok(ToProfileDetail(user));
    }

    /// <summary>
    ///     This action updates the caller's display name and phone number as an RFC 7396 merge
    ///     patch (see <see cref="JsonElementMergePatchExtension" />): an absent field is unchanged,
    ///     a JSON <c>null</c> clears it, and a string sets it. The e-mail address cannot be changed
    ///     here (see <see cref="UserProfileDetail" />). Requires only a signed-in caller.
    /// </summary>
    [HttpPatch("me")]
    [Authorize]
    [SwaggerRequestExample(typeof(JsonElement), typeof(UserProfilePatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(UserProfileDetailExample))]
    public async Task<ActionResult<UserProfileDetail>> PatchMine([FromBody] JsonElement patch)
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        if (patch.TryGetMergePatchField("displayName", out JsonElement displayNameElement))
            user.DisplayName =
                displayNameElement.ValueKind == JsonValueKind.Null
                    ? null
                    : displayNameElement.GetString();

        if (patch.TryGetMergePatchField("phoneNumber", out JsonElement phoneNumberElement))
            user.PhoneNumber =
                phoneNumberElement.ValueKind == JsonValueKind.Null
                    ? null
                    : phoneNumberElement.GetString();

        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;

        IdentityResult result = await userManager.UpdateAsync(user);
        result.ThrowIfFailed();

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return Ok(ToProfileDetail(user));
    }

    /// <summary>
    ///     This action changes the caller's password. It requires the current password; a wrong one
    ///     returns 400 with Identity's <c>PasswordMismatch</c> error. A successful change revokes
    ///     every refresh token of the account, including the caller's own session: the request
    ///     does not say which session it belongs to, so the caller's access token works until it
    ///     expires and the caller then signs in again with the new password. Requires only a
    ///     signed-in caller.
    /// </summary>
    [HttpPost("me/change-password")]
    [Authorize]
    [SwaggerRequestExample(typeof(ChangePasswordRequest), typeof(ChangePasswordRequestExample))]
    public async Task<IActionResult> ChangeMyPassword(
        ChangePasswordRequest request,
        CancellationToken stoppingToken
    )
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        IdentityResult result = await userManager.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword
        );
        result.ThrowIfFailed();

        await jwtTokenService.RevokeAllRefreshTokensAsync(user.Id, stoppingToken);

        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await userManager.UpdateAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action resets an account's password for an administrator, for an account that
    ///     cannot supply its current password as <see cref="ChangeMyPassword" /> requires.
    ///     <see cref="AdminResetPasswordRequest.Mode" /> picks one of two modes (see
    ///     <see cref="AdminPasswordResetMode" />). Requires <c>users:write</c>.
    ///     <list type="bullet">
    ///         <item>
    ///             <see cref="AdminPasswordResetMode.AutoGenerate" /> sets a generated strong
    ///             password at once, through a reset token and
    ///             <see cref="UserManager{TUser}.ResetPasswordAsync" /> so Identity's usual
    ///             validation and hashing apply, and returns it in this response only. It also revokes
    ///             every refresh token of the account.
    ///         </item>
    ///         <item>
    ///             <see cref="AdminPasswordResetMode.EmailLink" /> e-mails the account a link to
    ///             the admin console's "set new password" page, which submits to
    ///             <see cref="Controllers.AuthController.ResetPassword" />. The link carries an
    ///             Identity reset token, Base64Url-encoded for the query string (see
    ///             <see cref="ConfirmPasswordResetRequest" />). The account must have an e-mail
    ///             address, and SMTP and the public base URL the link points at must be
    ///             configured (see <c>SmtpSettingsController</c>); otherwise this returns 400
    ///             with a clear message.
    ///         </item>
    ///     </list>
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    [RequirePermission("users:write")]
    [SwaggerRequestExample(
        typeof(AdminResetPasswordRequest),
        typeof(AdminResetPasswordRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AdminResetPasswordResultExample))]
    public async Task<ActionResult<AdminResetPasswordResult>> ResetPassword(
        Guid id,
        AdminResetPasswordRequest request,
        CancellationToken stoppingToken
    )
    {
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            throw new NotFoundException("User", id.ToString());

        if (request.Mode == AdminPasswordResetMode.AutoGenerate)
        {
            string newPassword = PasswordGenerator.GenerateStrongRandomPassword(
                identityOptions.CurrentValue.Password
            );

            string resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            IdentityResult result = await userManager.ResetPasswordAsync(
                user,
                resetToken,
                newPassword
            );
            result.ThrowIfFailed();

            await jwtTokenService.RevokeAllRefreshTokensAsync(user.Id, stoppingToken);

            user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
            await userManager.UpdateAsync(user);

            eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

            return Ok(new AdminResetPasswordResult(request.Mode, newPassword));
        }

        // AdminPasswordResetMode.EmailLink from here down.
        if (string.IsNullOrWhiteSpace(user.Email))
            throw new ValidationException(
                "This account has no e-mail address to send a reset link to."
            );

        // A clear message here beats the raw exception SmtpClient.ConnectAsync would throw. An
        // unconfigured SMTP server is the normal state of a fresh homelab install.
        if (string.IsNullOrWhiteSpace(emailOptions.CurrentValue.SmtpHost))
            throw new ValidationException(
                "SMTP is not configured. Configure SMTP settings before using the emailed "
                    + "reset-link delivery mode, or use the auto-generated-password delivery "
                    + "mode instead."
            );

        // Throws a ValidationException naming the setting when no public base URL is configured.
        await passwordResetLinkSender.SendAsync(user, stoppingToken);

        return Ok(new AdminResetPasswordResult(request.Mode, null));
    }

    /// <summary>
    ///     This method builds <see cref="UserProfileDetail" /> for <paramref name="user" />.
    /// </summary>
    private static UserProfileDetail ToProfileDetail(UserEntity user) =>
        new(user.Id, user.UserName, user.Email, user.DisplayName, user.PhoneNumber);

    /// <summary>
    ///     This method loads the signed-in caller's <see cref="UserEntity" />, or returns
    ///     <see langword="null" /> when there is none.
    /// </summary>
    private async Task<UserEntity> GetCurrentUserAsync()
    {
        Guid? userId = User.GetUserId();

        return userId is null ? null : await userManager.FindByIdAsync(userId.Value.ToString());
    }

    /// <summary>
    ///     This method converts a user entity to its administrative API summary, with its role
    ///     memberships and its directly granted permissions (not those that come from roles).
    /// </summary>
    private async Task<UserSummary> ToSummaryAsync(UserEntity user)
    {
        IList<string> roles = await userManager.GetRolesAsync(user);
        IList<Claim> claims = await userManager.GetClaimsAsync(user);

        IReadOnlyList<string> directPermissions = claims
            .Where(claim => claim.Type == NinlilDnsClaimTypes.Permission)
            .Select(claim => claim.Value)
            .ToList();

        return new UserSummary(
            user.Id,
            user.UserName,
            user.Email,
            user.DisplayName,
            user.IsDisabled,
            user.TwoFactorEnabled,
            [.. roles],
            directPermissions,
            user.OriginatingNodeId,
            user.ClusterConflictNote,
            user.GetLastModifiedUtc()
        );
    }
}
