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
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Examples.Users;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Services;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers every user account on this server: creating, updating, and
///     deleting accounts, and granting or revoking the roles and direct permission claims that
///     decide what an account can do. Every administrative action requires the
///     <c>users:read</c> or <c>users:write</c> permission -- see
///     <see cref="RequirePermissionAttribute" />.
///     <para>
///         <b>The two self-service actions (<see cref="GetMine" />/<see cref="PatchMine" />) are
///         gated by <see cref="AuthorizeAttribute" /> alone</b>, the same "any signed-in caller may
///         freely manage their own account, no specific permission needed" bar
///         <see cref="AppearanceSettingsController" />'s own <c>GetMine</c>/<c>PatchMine</c> pair
///         and <see cref="TwoFactorController" />'s own account-security actions already set. They
///         expose only <see cref="UserEntity.DisplayName" />/<see cref="UserEntity.PhoneNumber" />
///         (plus a read-only <see cref="UserEntity.Email" />) for the Angular admin SPA's own
///         Profile page -- never role/permission/lockout state, which stays behind the
///         administrative actions above.
///     </para>
///     <para>
///         <b><see cref="ChangeMyPassword" /></b> is a third self-service action, gated the same
///         way, but it proves the caller already knows their own current password (via
///         <c>UserManager&lt;TUser&gt;.ChangePasswordAsync</c>) rather than trusting the bearer
///         token alone -- a bearer token proves who is signed in right now, not that the caller
///         still remembers the password protecting the account long-term.
///         <b><see cref="ResetPassword" /></b> is the administrator-driven counterpart, for a
///         locked-out account that cannot supply its own current password at all -- gated on
///         <c>users:write</c> like every other administrative action on this controller, never
///         self-service.
///     </para>
/// </summary>
/// <param name="userManager">Creates, updates, and deletes user accounts.</param>
/// <param name="userRepository">
///     Lists every user account -- <see cref="UserManager{TUser}.Users" /> is not available
///     since <c>NinlilDnsUserStore</c> does not implement <c>IQueryableUserStore</c>.
/// </param>
/// <param name="emailOptions">
///     The live, currently-effective SMTP server settings -- read fresh on every reset request
///     (not snapshotted at construction time) so a pre-flight "is SMTP even configured" check
///     always reflects the most recently saved <c>SmtpSettingsController.Patch</c>, the same
///     reasoning <c>SmtpEmailSender</c> itself already applies.
/// </param>
/// <param name="passwordResetLinkSender">
///     Builds and sends the actual reset e-mail once this controller has decided a reset link
///     should go out -- shared with the self-service Forgot Password flow
///     (<c>AuthController.ForgotPassword</c>) so both send the exact same message through the
///     exact same real token-generation/encode/link-build/send round trip.
/// </param>
/// <param name="eventPublisher">
///     The event bus this controller publishes a <see cref="UserAccountChangedEvent" /> through
///     on every mutation -- the cluster's own identity/RBAC federation
///     (<c>Ninlil.Dns.Server.ClusterSync</c>'s <c>UserClusterSyncEventSubscriber</c>) is its only
///     real subscriber, the same "no local engine needs this, only cluster propagation does"
///     shape <see cref="FilterListsController" /> already establishes.
/// </param>
/// <param name="usernameUniquenessChecker">
///     The cluster's own best-effort, cluster-wide username-uniqueness check, consulted only by
///     <see cref="Create" /> -- see <see cref="IClusterUsernameUniquenessChecker" />'s own doc
///     comment for why this never blocks account creation merely because one peer is
///     unreachable.
/// </param>
/// <param name="clusterOptions">
///     Read for this node's own stable cluster node id, stamped onto
///     <see cref="AuditableEntityBase.OriginatingNodeId" /> on every local mutation -- mirrors
///     <see cref="FilterListsController" />'s own identical convention, so an account's own "last
///     modified by node" indicator (and the last-write-wins tie-break a genuine cluster collision
///     needs) reflects this node's real identity rather than staying <see langword="null" />
///     forever for an account that only was ever changed locally.
/// </param>
[ApiController]
[Route("api/users")]
public class UsersController(
    UserManager<UserEntity> userManager,
    IUserRepository userRepository,
    IOptionsMonitor<EmailSenderOptions> emailOptions,
    PasswordResetLinkSender passwordResetLinkSender,
    IDnsServerEventPublisher eventPublisher,
    IClusterUsernameUniquenessChecker usernameUniquenessChecker,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists every user account on this server.
    /// </summary>
    [HttpGet]
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
    ///     This action looks up a single user account by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action creates a new user account. Before actually creating it, a best-effort,
    ///     cluster-wide uniqueness check (<see cref="IClusterUsernameUniquenessChecker" />) is
    ///     consulted against every currently-reachable peer -- a real, currently-healthy peer
    ///     that already reports this username rejects the request outright with a
    ///     <see cref="ConflictException" />, since that is a genuinely preventable collision, not
    ///     a partition; an unreachable peer is silently skipped, per this project's own explicit
    ///     "never block user management on one down peer" decision. A rarer collision that
    ///     genuinely only becomes visible after both sides already committed (a true network
    ///     partition) is instead caught and resolved later, once the two nodes resync, by
    ///     <c>UserClusterConfigSyncApplier</c>'s own last-write-wins collision handling.
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
    ///     This action updates an existing user account's profile and enabled/disabled state.
    ///     Password changes are handled separately, not through this endpoint.
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
    ///     This action permanently deletes a user account.
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
    ///     This action grants a user account a role.
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

        IdentityResult result = await userManager.AddToRoleAsync(user, request.RoleName);
        result.ThrowIfFailed();

        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await userManager.UpdateAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action revokes a role from a user account.
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
    ///     This action grants a user account a permission claim directly, independent of any
    ///     role they hold.
    /// </summary>
    [HttpPost("{id:guid}/permissions")]
    [RequirePermission("users:write")]
    [SwaggerRequestExample(typeof(UserPermissionRequest), typeof(UserPermissionRequestExample))]
    public async Task<IActionResult> AddPermission(Guid id, UserPermissionRequest request)
    {
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            throw new NotFoundException("User", id.ToString());

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
    ///     This action revokes a permission claim that was granted to a user account directly.
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
    ///     This action reports the current caller's own self-service profile: display name, phone
    ///     number, and (read-only) e-mail address. Unlike <see cref="Get" />, this needs no
    ///     <c>users:read</c> permission -- see this class's own doc comment.
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
    ///     This action merges a partial update into the current caller's own
    ///     <see cref="UserEntity.DisplayName" />/<see cref="UserEntity.PhoneNumber" />, RFC 7396
    ///     merge-patch style (see <see cref="JsonElementMergePatchExtension" />): a field absent
    ///     from <paramref name="patch" /> is left exactly as it was; a field present with a JSON
    ///     <c>null</c> clears it back to <see langword="null" />; a field present with a string
    ///     value sets it. <see cref="UserEntity.Email" /> is deliberately not accepted here -- see
    ///     this class's own doc comment and <see cref="UserProfileDetail" />'s own doc comment for
    ///     why.
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
    ///     This action changes the current caller's own password, proving they already know the
    ///     current one via <see cref="UserManager{TUser}.ChangePasswordAsync" /> -- a wrong
    ///     <see cref="ChangePasswordRequest.CurrentPassword" /> surfaces as a real, specific
    ///     <see cref="IdentityResult" /> failure (Identity's own <c>PasswordMismatch</c> error)
    ///     through <see cref="IdentityResultExtension.ThrowIfFailed" />, never swallowed into a
    ///     generic message. See this class's own doc comment for why this needs no specific
    ///     permission, only <see cref="AuthorizeAttribute" />.
    /// </summary>
    [HttpPost("me/change-password")]
    [Authorize]
    [SwaggerRequestExample(typeof(ChangePasswordRequest), typeof(ChangePasswordRequestExample))]
    public async Task<IActionResult> ChangeMyPassword(ChangePasswordRequest request)
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

        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await userManager.UpdateAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action resets a user account's password on an administrator's behalf -- built for
    ///     a locked-out account that cannot supply its own current password the way
    ///     <see cref="ChangeMyPassword" /> requires. <see cref="AdminResetPasswordRequest.Mode" />
    ///     selects one of two delivery modes (see <see cref="AdminPasswordResetMode" />'s own doc
    ///     comment for what each does and why both exist):
    ///     <list type="bullet">
    ///         <item>
    ///             <see cref="AdminPasswordResetMode.AutoGenerate" /> sets a freshly-generated
    ///             strong password immediately (via a real
    ///             <see cref="UserManager{TUser}.GeneratePasswordResetTokenAsync" />/
    ///             <see cref="UserManager{TUser}.ResetPasswordAsync" /> round trip, not a direct
    ///             field write, so the exact same Identity validation/hashing path every other
    ///             password change goes through still applies) and returns it exactly once, in
    ///             the response body only -- this project's usual reveal-once secret exposure
    ///             policy.
    ///         </item>
    ///         <item>
    ///             <see cref="AdminPasswordResetMode.EmailLink" /> e-mails the account's own
    ///             address a link to this project's shared "set new password" landing page
    ///             (<see cref="Controllers.AuthController.ResetPassword" /> is what that page's
    ///             own form submits to), carrying a real Identity reset token, Base64Url-encoded
    ///             for safe transport in a URL query string -- see
    ///             <see cref="ConfirmPasswordResetRequest" />'s own doc comment for why. Requires
    ///             both a confirmed e-mail address on the target account and an already-configured
    ///             SMTP server (see <c>SmtpSettingsController</c>); either gap is reported as a
    ///             clear <see cref="ValidationException" /> rather than letting
    ///             <c>SmtpClient.ConnectAsync</c> fail with an unhelpful raw exception.
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
            string newPassword = PasswordGenerator.GenerateStrongRandomPassword();

            string resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            IdentityResult result = await userManager.ResetPasswordAsync(
                user,
                resetToken,
                newPassword
            );
            result.ThrowIfFailed();

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

        // Fail with a clear, actionable message rather than letting SmtpClient.ConnectAsync
        // throw an unhelpful raw exception when no SMTP server has ever been configured -- a
        // real, common state for this project's homelab-first deployment default.
        if (string.IsNullOrWhiteSpace(emailOptions.CurrentValue.SmtpHost))
            throw new ValidationException(
                "SMTP is not configured. Configure SMTP settings before using the emailed "
                    + "reset-link delivery mode, or use the auto-generated-password delivery "
                    + "mode instead."
            );

        await passwordResetLinkSender.SendAsync(user, Request.Scheme, Request.Host, stoppingToken);

        return Ok(new AdminResetPasswordResult(request.Mode, null));
    }

    /// <summary>
    ///     This method builds <see cref="UserProfileDetail" /> for <paramref name="user" />.
    /// </summary>
    private static UserProfileDetail ToProfileDetail(UserEntity user) =>
        new(user.Id, user.UserName, user.Email, user.DisplayName, user.PhoneNumber);

    /// <summary>
    ///     This method resolves the currently signed-in caller's own <see cref="UserEntity" />,
    ///     the same pattern <see cref="TwoFactorController.GetCurrentUserAsync" /> and
    ///     <see cref="AppearanceSettingsController" />'s own equivalent already establish for a
    ///     self-service action.
    /// </summary>
    private async Task<UserEntity> GetCurrentUserAsync()
    {
        Guid? userId = User.GetUserId();

        return userId is null ? null : await userManager.FindByIdAsync(userId.Value.ToString());
    }

    /// <summary>
    ///     This method converts a user entity into its administrative REST representation,
    ///     resolving both its role memberships and its own directly-granted permission claims
    ///     (distinct from whatever permissions those roles separately grant).
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
