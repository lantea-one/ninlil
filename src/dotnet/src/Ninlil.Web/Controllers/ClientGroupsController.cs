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
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this server's own client groups -- the top-level
///     resource; a group's own networks and filter-list assignments are managed separately,
///     through the nested <see cref="ClientGroupNetworksController" />
///     (<c>/api/client-groups/{groupId}/networks</c>) and
///     <see cref="ClientGroupListAssignmentsController" />
///     (<c>/api/client-groups/{groupId}/list-assignments</c>). Every action requires the
///     <c>clientgroups:read</c> or <c>clientgroups:write</c> permission -- reused for both nested
///     controllers too, the same way <c>zones:read</c>/<c>zones:write</c> already cover
///     <see cref="RecordsController" /> as a zone's own child resource.
/// </summary>
/// <param name="clientGroupRepository">The repository this controller reads and writes groups through.</param>
/// <param name="networkRepository">The repository used to remove a deleted group's own networks -- see <see cref="Delete" />.</param>
/// <param name="listAssignmentRepository">The repository used to remove a deleted group's own list assignments -- see <see cref="Delete" />.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
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
    ///     This action lists every client group this server knows about.
    /// </summary>
    [HttpGet]
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
    ///     This action looks up a single client group by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action creates a new client group. When <see cref="ClientGroupWriteRequest.IsDefault" />
    ///     is <see langword="true" />, whichever other group currently holds
    ///     <see cref="DnsClientGroupEntity.IsDefault" /> is demoted first -- see
    ///     <see cref="DemoteExistingDefaultAsync" /> for why exactly one group may hold it at a
    ///     time.
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
    ///     This action fully replaces an existing client group's metadata.
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
    ///     This action partially updates an existing client group's metadata via RFC 7396 JSON
    ///     Merge Patch: a field absent from the patch document is left untouched, and any other
    ///     present field overwrites it.
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
    ///     This action permanently deletes a client group, every network it contains, and every
    ///     filter-list assignment it holds -- removed explicitly, rather than relying on the
    ///     relational backend's own cascade delete, since MongoDB has no equivalent mechanism at
    ///     all, the same reasoning <see cref="ZonesController.Delete" /> already applies to a
    ///     deleted zone's own records and DNSSEC keys.
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
    ///     This method clears <see cref="DnsClientGroupEntity.IsDefault" /> on whichever other
    ///     group currently holds it. <c>DnsFilterEngine.GetDefaultAsync</c>'s own contract expects
    ///     at most one group to ever hold it -- letting two groups both claim to be the fallback
    ///     would leave which one the engine actually picks unspecified (a real ambiguity, not
    ///     merely untidy data), so this project enforces "at most one default" here rather than
    ///     leaving it to the operator's own discipline.
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
    ///     This action starts, or replaces, an instant pause on a client group -- blocking every
    ///     one of its members' own DNS queries outright, regardless of any schedule, filter-list,
    ///     or rewrite that would otherwise apply, until <see cref="InstantPauseRequest.DurationMinutes" />
    ///     minutes from now. Mirrors <see cref="ClientsController.Pause" /> exactly, at the group
    ///     level instead of the client level -- see that action's own doc comment.
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
    ///     This action ends a client group's own instant pause immediately, clearing
    ///     <see cref="DnsClientGroupEntity.PausedUntilUtc" /> back to <see langword="null" />
    ///     regardless of how much time was left on it. A no-op, not an error, when the group was
    ///     not currently paused at all.
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
    ///     This method converts a client group entity into its REST representation.
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
