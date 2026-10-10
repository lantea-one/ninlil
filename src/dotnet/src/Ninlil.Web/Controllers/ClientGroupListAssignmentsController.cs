using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientGroupListAssignments;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages which block and allow lists apply to one client group, nested
///     under it at <c>/api/client-groups/{groupId}/list-assignments</c>. Reads need
///     <c>clientgroups:read</c> and changes need <c>clientgroups:write</c>, as for
///     <see cref="ClientGroupsController" />. There is no PUT or PATCH: an assignment only joins a
///     group to a list, so changing the list means deleting one assignment and creating another
///     (see <see cref="ClientGroupListAssignmentWriteRequest" />).
/// </summary>
/// <param name="clientGroupRepository">Confirms the group exists.</param>
/// <param name="filterListRepository">Confirms the assigned list exists.</param>
/// <param name="listAssignmentRepository">Reads and writes assignments.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
[ApiController]
[Route("api/client-groups/{groupId:guid}/list-assignments")]
public class ClientGroupListAssignmentsController(
    IDnsClientGroupRepository clientGroupRepository,
    IDnsFilterListRepository filterListRepository,
    IDnsClientGroupListAssignmentRepository listAssignmentRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists a group's list assignments, one page at a time. Returns 404 for an
    ///     unknown group. Requires <c>clientgroups:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("clientgroups:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(ClientGroupListAssignmentDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<ClientGroupListAssignmentDetail>>> List(
        Guid groupId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await clientGroupRepository.GetByIdAsync(groupId, stoppingToken) is null)
            throw new NotFoundException("ClientGroup", groupId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsClientGroupListAssignmentEntity> assignments =
            await listAssignmentRepository.SearchByGroupIdAsync(groupId, skip, take, stoppingToken);
        int totalCount = await listAssignmentRepository.CountByGroupIdAsync(groupId, stoppingToken);

        return Ok(
            new PagedResult<ClientGroupListAssignmentDetail>(
                [.. assignments.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action returns one list assignment. Returns 404 when it does not exist or belongs
    ///     to another group. Requires <c>clientgroups:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("clientgroups:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(ClientGroupListAssignmentDetailExample)
    )]
    public async Task<ActionResult<ClientGroupListAssignmentDetail>> Get(
        Guid groupId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupListAssignmentEntity assignment = await listAssignmentRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (assignment is null || assignment.ClientGroupId != groupId)
            throw new NotFoundException("ClientGroupListAssignment", id.ToString());

        return Ok(ToDetail(assignment));
    }

    /// <summary>
    ///     This action assigns a block or allow list to the group. Returns 400 when the list does
    ///     not exist (an invalid value in the request, not a missing resource), 404 for an unknown
    ///     group, and 409 when the list is already assigned. A duplicate would not change
    ///     filtering, since <c>DnsFilterEngine.ResolveApplicableListsAsync</c> collects list ids
    ///     into a <see cref="HashSet{T}" />, but it is refused to keep rows meaningful. Requires
    ///     <c>clientgroups:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("clientgroups:write")]
    [SwaggerRequestExample(
        typeof(ClientGroupListAssignmentWriteRequest),
        typeof(ClientGroupListAssignmentWriteRequestExample)
    )]
    [SwaggerResponseExample(
        StatusCodes.Status201Created,
        typeof(ClientGroupListAssignmentDetailExample)
    )]
    public async Task<ActionResult<ClientGroupListAssignmentDetail>> Create(
        Guid groupId,
        ClientGroupListAssignmentWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (await clientGroupRepository.GetByIdAsync(groupId, stoppingToken) is null)
            throw new NotFoundException("ClientGroup", groupId.ToString());

        if (request is null || request.ListId == Guid.Empty)
            throw new ValidationException("'listId' is required.");

        if (await filterListRepository.GetByIdAsync(request.ListId, stoppingToken) is null)
            throw new ValidationException($"No filter list with id '{request.ListId}' exists.");

        IReadOnlyList<DnsClientGroupListAssignmentEntity> existing =
            await listAssignmentRepository.ListByGroupIdAsync(groupId, stoppingToken);
        if (existing.Any(assignment => assignment.ListId == request.ListId))
            throw new ConflictException(
                $"List '{request.ListId}' is already assigned to this client group."
            );

        DnsClientGroupListAssignmentEntity newAssignment = new()
        {
            Id = Guid.NewGuid(),
            ClientGroupId = groupId,
            ListId = request.ListId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await listAssignmentRepository.AddAsync(newAssignment, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(
            nameof(Get),
            new { groupId, id = newAssignment.Id },
            ToDetail(newAssignment)
        );
    }

    /// <summary>
    ///     This action removes a list assignment from the group. Returns 404 when it does not exist
    ///     or belongs to another group. Requires <c>clientgroups:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clientgroups:write")]
    public async Task<IActionResult> Delete(Guid groupId, Guid id, CancellationToken stoppingToken)
    {
        DnsClientGroupListAssignmentEntity assignment = await listAssignmentRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (assignment is null || assignment.ClientGroupId != groupId)
            throw new NotFoundException("ClientGroupListAssignment", id.ToString());

        listAssignmentRepository.Remove(assignment);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method converts an assignment entity to its API representation.
    /// </summary>
    private static ClientGroupListAssignmentDetail ToDetail(
        DnsClientGroupListAssignmentEntity assignment
    ) => new(assignment.Id, assignment.ClientGroupId, assignment.ListId, assignment.CreatedAtUtc);
}
