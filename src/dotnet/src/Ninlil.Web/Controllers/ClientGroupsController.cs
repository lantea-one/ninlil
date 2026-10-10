using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientGroups;
using Ninlil.Web.Examples.Clients;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages client groups. A group's networks and filter-list assignments are
///     managed through the nested <see cref="ClientGroupNetworksController" />
///     (<c>/api/client-groups/{groupId}/networks</c>) and
///     <see cref="ClientGroupListAssignmentsController" />
///     (<c>/api/client-groups/{groupId}/list-assignments</c>), which use the same permissions:
///     <c>clientgroups:read</c> for reads and <c>clientgroups:write</c> for changes. It also
///     pauses and resumes a whole group's DNS.
/// </summary>
/// <param name="clientGroupRepository">Reads and writes groups.</param>
/// <param name="networkRepository">Removes a deleted group's networks (see <see cref="Delete" />).</param>
/// <param name="listAssignmentRepository">Removes a deleted group's list assignments (see <see cref="Delete" />).</param>
/// <param name="unitOfWork">Commits staged changes.</param>
[ApiController]
[Route("api/client-groups")]
public class ClientGroupsController(
    IDnsClientGroupRepository clientGroupRepository,
    IDnsClientGroupNetworkRepository networkRepository,
    IDnsClientGroupListAssignmentRepository listAssignmentRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists client groups, one page at a time. Requires <c>clientgroups:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("clientgroups:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientGroupDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<ClientGroupDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsClientGroupEntity> groups = await clientGroupRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await clientGroupRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<ClientGroupDetail>([.. groups.Select(ToDetail)], skip, take, totalCount)
        );
    }

    /// <summary>
    ///     This action returns one client group. Returns 404 for an unknown id. Requires
    ///     <c>clientgroups:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("clientgroups:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientGroupDetailExample))]
    public async Task<ActionResult<ClientGroupDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        DnsClientGroupEntity group = await clientGroupRepository.GetByIdAsync(id, stoppingToken);
        if (group is null)
            throw new NotFoundException("ClientGroup", id.ToString());

        return Ok(ToDetail(group));
    }

    /// <summary>
    ///     This action creates a client group. When
    ///     <see cref="ClientGroupWriteRequest.IsDefault" /> is <see langword="true" />, the group
    ///     that currently holds <see cref="DnsClientGroupEntity.IsDefault" /> loses it (see
    ///     <see cref="DemoteExistingDefaultAsync" />). Returns 400 for a missing name and 409 when
    ///     the name is taken. Requires <c>clientgroups:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("clientgroups:write")]
    [SwaggerRequestExample(typeof(ClientGroupWriteRequest), typeof(ClientGroupWriteRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(ClientGroupDetailExample))]
    public async Task<ActionResult<ClientGroupDetail>> Create(
        ClientGroupWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        if (await clientGroupRepository.FindByNameAsync(request.Name, stoppingToken) is not null)
            throw new ConflictException($"A client group named '{request.Name}' already exists.");

        DnsClientGroupEntity group = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            IsDefault = request.IsDefault,
            IsDisabled = request.IsDisabled,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        if (request.IsDefault)
            await DemoteExistingDefaultAsync(group.Id, stoppingToken);

        await clientGroupRepository.AddAsync(group, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(nameof(Get), new { id = group.Id }, ToDetail(group));
    }

    /// <summary>
    ///     This action replaces a client group's name, default flag and disabled flag; making it
    ///     the default takes the flag from the current default group. Returns 400 for a missing
    ///     name, 404 for an unknown id and 409 when the name is taken. Requires
    ///     <c>clientgroups:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("clientgroups:write")]
    [SwaggerRequestExample(typeof(ClientGroupWriteRequest), typeof(ClientGroupWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid id,
        ClientGroupWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupEntity group = await clientGroupRepository.GetByIdAsync(id, stoppingToken);
        if (group is null)
            throw new NotFoundException("ClientGroup", id.ToString());

        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        DnsClientGroupEntity existingByName = await clientGroupRepository.FindByNameAsync(
            request.Name,
            stoppingToken
        );
        if (existingByName is not null && existingByName.Id != id)
            throw new ConflictException($"A client group named '{request.Name}' already exists.");

        if (request.IsDefault && !group.IsDefault)
            await DemoteExistingDefaultAsync(id, stoppingToken);

        group.Name = request.Name;
        group.IsDefault = request.IsDefault;
        group.IsDisabled = request.IsDisabled;
        group.UpdatedAtUtc = DateTimeOffset.UtcNow;

        clientGroupRepository.Update(group);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action updates a client group as an RFC 7396 merge patch: an absent field is
    ///     unchanged and a present one replaces it. Errors are as for <see cref="Replace" />.
    ///     Requires <c>clientgroups:write</c>.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("clientgroups:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ClientGroupPatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupEntity group = await clientGroupRepository.GetByIdAsync(id, stoppingToken);
        if (group is null)
            throw new NotFoundException("ClientGroup", id.ToString());

        if (patch.TryGetMergePatchField("name", out JsonElement name))
        {
            string newName = name.GetString();
            if (string.IsNullOrWhiteSpace(newName))
                throw new ValidationException("'name' must not be empty.");

            DnsClientGroupEntity existingByName = await clientGroupRepository.FindByNameAsync(
                newName,
                stoppingToken
            );
            if (existingByName is not null && existingByName.Id != id)
                throw new ConflictException($"A client group named '{newName}' already exists.");

            group.Name = newName;
        }

        if (patch.TryGetMergePatchField("isDefault", out JsonElement isDefault))
        {
            bool newIsDefault = isDefault.GetBoolean();
            if (newIsDefault && !group.IsDefault)
                await DemoteExistingDefaultAsync(id, stoppingToken);

            group.IsDefault = newIsDefault;
        }

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            group.IsDisabled = isDisabled.GetBoolean();

        group.UpdatedAtUtc = DateTimeOffset.UtcNow;
        clientGroupRepository.Update(group);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action deletes a client group with its networks and filter-list assignments. They
    ///     are removed explicitly rather than by a relational cascade delete, which MongoDB lacks,
    ///     as <see cref="ZonesController.Delete" /> does. Returns 404 for an unknown id. Requires
    ///     <c>clientgroups:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clientgroups:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DnsClientGroupEntity group = await clientGroupRepository.GetByIdAsync(id, stoppingToken);
        if (group is null)
            throw new NotFoundException("ClientGroup", id.ToString());

        IReadOnlyList<DnsClientGroupNetworkEntity> networks =
            await networkRepository.ListByGroupIdAsync(id, stoppingToken);
        foreach (DnsClientGroupNetworkEntity network in networks)
            networkRepository.Remove(network);

        IReadOnlyList<DnsClientGroupListAssignmentEntity> assignments =
            await listAssignmentRepository.ListByGroupIdAsync(id, stoppingToken);
        foreach (DnsClientGroupListAssignmentEntity assignment in assignments)
            listAssignmentRepository.Remove(assignment);

        clientGroupRepository.Remove(group);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method clears <see cref="DnsClientGroupEntity.IsDefault" /> on the other group that
    ///     holds it. <c>DnsFilterEngine.GetDefaultAsync</c> expects at most one default group; with
    ///     two, which one the engine picks would be undefined, so the rule is enforced here.
    /// </summary>
    private async Task DemoteExistingDefaultAsync(Guid excludingId, CancellationToken stoppingToken)
    {
        DnsClientGroupEntity currentDefault = await clientGroupRepository.GetDefaultAsync(
            stoppingToken
        );
        if (currentDefault is null || currentDefault.Id == excludingId)
            return;

        currentDefault.IsDefault = false;
        currentDefault.UpdatedAtUtc = DateTimeOffset.UtcNow;
        clientGroupRepository.Update(currentDefault);
    }

    /// <summary>
    ///     This action pauses a client group now: the DNS queries of every known client whose
    ///     <c>ClientGroupId</c> is this group are blocked, whatever schedules, filter lists or
    ///     rewrites would apply, for
    ///     <see cref="InstantPauseRequest.DurationMinutes" /> minutes, as
    ///     <see cref="ClientsController.Pause" /> does for one client. Calling it during a pause
    ///     replaces the pause. Returns 400 for a duration that is not positive and 404 for an
    ///     unknown id. Requires <c>clientgroups:write</c>.
    /// </summary>
    [HttpPost("{id:guid}/pause")]
    [RequirePermission("clientgroups:write")]
    [SwaggerRequestExample(typeof(InstantPauseRequest), typeof(ClientPauseRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientGroupDetailExample))]
    public async Task<ActionResult<ClientGroupDetail>> Pause(
        Guid id,
        InstantPauseRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupEntity group = await clientGroupRepository.GetByIdAsync(id, stoppingToken);
        if (group is null)
            throw new NotFoundException("ClientGroup", id.ToString());

        if (request is null || request.DurationMinutes <= 0)
            throw new ValidationException(
                "'durationMinutes' must be a positive number of minutes."
            );

        group.PausedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(request.DurationMinutes);
        group.UpdatedAtUtc = DateTimeOffset.UtcNow;

        clientGroupRepository.Update(group);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return Ok(ToDetail(group));
    }

    /// <summary>
    ///     This action ends a client group's pause now by clearing
    ///     <see cref="DnsClientGroupEntity.PausedUntilUtc" />. Resuming a group that is not paused
    ///     is not an error. Returns 404 for an unknown id. Requires <c>clientgroups:write</c>.
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    [RequirePermission("clientgroups:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientGroupDetailExample))]
    public async Task<ActionResult<ClientGroupDetail>> Resume(
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupEntity group = await clientGroupRepository.GetByIdAsync(id, stoppingToken);
        if (group is null)
            throw new NotFoundException("ClientGroup", id.ToString());

        group.PausedUntilUtc = null;
        group.UpdatedAtUtc = DateTimeOffset.UtcNow;

        clientGroupRepository.Update(group);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return Ok(ToDetail(group));
    }

    /// <summary>
    ///     This method converts a client group entity to its API representation.
    /// </summary>
    private static ClientGroupDetail ToDetail(DnsClientGroupEntity group)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new ClientGroupDetail(
            group.Id,
            group.Name,
            group.IsDefault,
            group.IsDisabled,
            group.CreatedAtUtc,
            group.UpdatedAtUtc,
            group.PausedUntilUtc,
            group.PausedUntilUtc is { } pausedUntilUtc && pausedUntilUtc > now
        );
    }
}
