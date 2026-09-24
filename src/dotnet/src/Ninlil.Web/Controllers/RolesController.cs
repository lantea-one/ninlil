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
///     This controller administers every role on this server: creating, updating, and deleting
///     roles, and granting or revoking the permission claims a role grants to every user who
///     holds it. Every action requires the <c>roles:read</c> or <c>roles:write</c> permission --
///     see <see cref="RequirePermissionAttribute" />.
/// </summary>
/// <param name="roleManager">Creates, updates, and deletes roles, and manages their claims.</param>
/// <param name="roleRepository">
///     Lists every role -- <see cref="RoleManager{TRole}" /> has no equivalent of
///     <c>UserManager&lt;TUser&gt;.Users</c> at all.
/// </param>
/// <param name="eventPublisher">
///     The event bus this controller publishes a <see cref="RoleChangedEvent" /> through on every
///     mutation -- the cluster's own identity/RBAC federation
///     (<c>Ninlil.Dns.Server.ClusterSync</c>'s <c>RoleClusterSyncEventSubscriber</c>) is its only
///     real subscriber.
/// </param>
/// <param name="clusterOptions">
///     Read for this node's own stable cluster node id, stamped onto <see cref="AuditableEntityBase.OriginatingNodeId" />
///     on every local mutation -- mirrors <c>FilterListsController</c>'s own identical convention,
///     so a role's own "last modified by node" indicator (and the last-write-wins tie-break a
///     genuine cluster collision needs) reflects this node's real identity rather than staying
///     <see langword="null" /> forever for a role that only was ever changed locally.
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
    ///     This action lists every role on this server.
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
    ///     This action looks up a single role by its identifier.
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
    ///     This action creates a new role.
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
    ///     This action updates an existing role's description.
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
    ///     This action permanently deletes a role. Any user currently holding it simply stops
    ///     holding it; their account itself is not affected.
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
    ///     This action grants a role a permission claim, extending that permission to every user
    ///     who holds the role.
    /// </summary>
    [HttpPost("{id:guid}/permissions")]
    [RequirePermission("roles:write")]
    [SwaggerRequestExample(typeof(RolePermissionRequest), typeof(RolePermissionRequestExample))]
    public async Task<IActionResult> AddPermission(Guid id, RolePermissionRequest request)
    {
        RoleEntity role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
            throw new NotFoundException("Role", id.ToString());

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
    ///     This action revokes a permission claim from a role.
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
    ///     This method converts a role entity into its REST representation, resolving its
    ///     currently-granted permission claims (via <see cref="RoleManager{TRole}.GetClaimsAsync" />)
    ///     into a plain list of permission strings.
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
