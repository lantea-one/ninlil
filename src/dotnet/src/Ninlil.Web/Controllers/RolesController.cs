using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Examples.Roles;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages roles: creating, updating and deleting them, and granting or
///     revoking the permissions a role gives every user who holds it. Reads need
///     <c>roles:read</c> and changes need <c>roles:write</c>. A permission change reaches a
///     signed-in user when their next access token is issued (at sign-in or refresh), since
///     permissions are carried as claims in the token.
/// </summary>
/// <param name="roleManager">Creates, updates and deletes roles, and manages their claims.</param>
/// <param name="roleRepository">
///     Lists roles; <see cref="RoleManager{TRole}" /> has no equivalent of
///     <c>UserManager&lt;TUser&gt;.Users</c>.
/// </param>
/// <param name="eventPublisher">
///     Publishes a <see cref="RoleChangedEvent" /> on every change, which
///     <c>RoleClusterSyncEventSubscriber</c> in <c>Ninlil.Dns.Server.ClusterSync</c> replicates
///     to the other cluster nodes.
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, stamped onto
///     <see cref="AuditableEntityBase.OriginatingNodeId" /> on every local change so the role
///     shows which node changed it last and cluster sync can break a last-write-wins tie.
/// </param>
[ApiController]
[Route("api/roles")]
public class RolesController(
    RoleManager<RoleEntity> roleManager,
    IRoleRepository roleRepository,
    IDnsServerEventPublisher eventPublisher,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists roles, one page at a time, with their permissions. Requires
    ///     <c>roles:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("roles:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RoleSummaryPagedResultExample))]
    public async Task<ActionResult<PagedResult<RoleSummary>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<RoleEntity> roles = await roleRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await roleRepository.CountAsync(stoppingToken);

        List<RoleSummary> summaries = [];
        foreach (RoleEntity role in roles)
            summaries.Add(await ToSummaryAsync(role));

        return Ok(new PagedResult<RoleSummary>(summaries, skip, take, totalCount));
    }

    /// <summary>
    ///     This action returns one role and its permissions. Returns 404 for an unknown id.
    ///     Requires <c>roles:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("roles:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RoleSummaryExample))]
    public async Task<ActionResult<RoleSummary>> Get(Guid id)
    {
        RoleEntity role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
            throw new NotFoundException("Role", id.ToString());

        return Ok(await ToSummaryAsync(role));
    }

    /// <summary>
    ///     This action creates a role with no permissions. Returns 400 when the name is invalid or
    ///     already taken. Requires <c>roles:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("roles:write")]
    [SwaggerRequestExample(typeof(CreateRoleRequest), typeof(CreateRoleRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(RoleSummaryExample))]
    public async Task<ActionResult<RoleSummary>> Create(CreateRoleRequest request)
    {
        RoleEntity role = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            OriginatingNodeId = clusterOptions.CurrentValue.NodeId,
        };

        IdentityResult result = await roleManager.CreateAsync(role);
        result.ThrowIfFailed();

        eventPublisher.Publish(new RoleChangedEvent(DateTimeOffset.UtcNow, role.Id));

        return CreatedAtAction(nameof(Get), new { id = role.Id }, await ToSummaryAsync(role));
    }

    /// <summary>
    ///     This action updates a role's description; the name cannot be changed. Requires
    ///     <c>roles:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("roles:write")]
    [SwaggerRequestExample(typeof(UpdateRoleRequest), typeof(UpdateRoleRequestExample))]
    public async Task<IActionResult> Update(Guid id, UpdateRoleRequest request)
    {
        RoleEntity role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
            throw new NotFoundException("Role", id.ToString());

        role.Description = request.Description;
        role.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;

        IdentityResult result = await roleManager.UpdateAsync(role);
        result.ThrowIfFailed();

        eventPublisher.Publish(new RoleChangedEvent(DateTimeOffset.UtcNow, role.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action deletes a role. Users who held it lose it and its permissions; their
    ///     accounts are otherwise unchanged. Requires <c>roles:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("roles:write")]
    public async Task<IActionResult> Delete(Guid id)
    {
        RoleEntity role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
            throw new NotFoundException("Role", id.ToString());

        await roleManager.DeleteAsync(role);

        eventPublisher.Publish(new RoleChangedEvent(DateTimeOffset.UtcNow, id, IsDeleted: true));

        return NoContent();
    }

    /// <summary>
    ///     This action grants a role a permission, giving it to every user who holds the role. The
    ///     permission string is not checked against <c>GET /api/permissions</c>. Requires
    ///     <c>roles:write</c>, and the caller must hold the permission it grants (only a holder of
    ///     <c>*</c> can grant <c>*</c>); otherwise this returns 403 naming the missing permission.
    /// </summary>
    [HttpPost("{id:guid}/permissions")]
    [RequirePermission("roles:write")]
    [SwaggerRequestExample(typeof(RolePermissionRequest), typeof(RolePermissionRequestExample))]
    public async Task<IActionResult> AddPermission(Guid id, RolePermissionRequest request)
    {
        RoleEntity role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
            throw new NotFoundException("Role", id.ToString());

        ObjectResult refusal = this.RefuseUnheldPermissions([request.Permission]);
        if (refusal is not null)
            return refusal;

        await roleManager.AddClaimAsync(
            role,
            new Claim(NinlilDnsClaimTypes.Permission, request.Permission)
        );

        role.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await roleManager.UpdateAsync(role);

        eventPublisher.Publish(new RoleChangedEvent(DateTimeOffset.UtcNow, role.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action revokes a permission from a role. Requires <c>roles:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}/permissions")]
    [RequirePermission("roles:write")]
    [SwaggerRequestExample(typeof(RolePermissionRequest), typeof(RolePermissionRequestExample))]
    public async Task<IActionResult> RemovePermission(Guid id, RolePermissionRequest request)
    {
        RoleEntity role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
            throw new NotFoundException("Role", id.ToString());

        await roleManager.RemoveClaimAsync(
            role,
            new Claim(NinlilDnsClaimTypes.Permission, request.Permission)
        );

        role.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await roleManager.UpdateAsync(role);

        eventPublisher.Publish(new RoleChangedEvent(DateTimeOffset.UtcNow, role.Id));

        return NoContent();
    }

    /// <summary>
    ///     This method converts a role entity to its API summary, including its permission claims
    ///     as a list of strings.
    /// </summary>
    private async Task<RoleSummary> ToSummaryAsync(RoleEntity role)
    {
        IList<Claim> claims = await roleManager.GetClaimsAsync(role);

        IReadOnlyList<string> permissions = claims
            .Where(claim => claim.Type == NinlilDnsClaimTypes.Permission)
            .Select(claim => claim.Value)
            .ToList();

        return new RoleSummary(
            role.Id,
            role.Name,
            role.Description,
            permissions,
            role.OriginatingNodeId,
            role.ClusterConflictNote,
            role.GetLastModifiedUtc()
        );
    }
}
