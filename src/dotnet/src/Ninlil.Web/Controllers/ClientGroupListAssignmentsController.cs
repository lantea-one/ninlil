using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientGroupListAssignments;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers which block/allow lists apply to a single client
///     group, nested under it (<c>/api/client-groups/{groupId}/list-assignments</c>) since an
///     assignment only ever makes sense in its owning group's own context -- the same nesting
///     <see cref="RecordsController" /> already establishes for a zone's records. Every action
///     requires the <c>clientgroups:read</c> or <c>clientgroups:write</c> permission, the same as
///     <see cref="ClientGroupsController" />. There is deliberately no <c>PUT</c>/<c>PATCH</c>
///     here -- an assignment is just a join between a group and a list with no mutable field of
///     its own; changing which list is assigned means deleting one assignment and creating
///     another, exactly like <see cref="ClientGroupListAssignmentWriteRequest" /> already
///     documents.
/// </summary>
/// <param name="clientGroupRepository">The repository used to confirm the owning group exists.</param>
/// <param name="filterListRepository">The repository used to confirm the assigned list actually exists.</param>
/// <param name="listAssignmentRepository">The repository this controller reads and writes assignments through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
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
    ///     This action lists every list assignment belonging to a client group.
    /// </summary>
    [HttpGet]
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
    ///     This action looks up a single list assignment by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action assigns a block/allow list to the client group. Rejected with
    ///     <c>400 Bad Request</c> if the referenced list does not exist -- treated as an invalid
    ///     value on the assignment being created, not a lookup of the list itself, so this stays a
    ///     <see cref="ValidationException" /> rather than a <see cref="NotFoundException" /> -- and
    ///     <c>409 Conflict</c> if this exact group/list pairing is already assigned -- an
    ///     unenforced duplicate would be harmless to <c>DnsFilterEngine</c> itself (it collects
    ///     assigned list identifiers into a <see cref="HashSet{T}" /> before resolving them, see
    ///     <c>DnsFilterEngine.ResolveApplicableListsAsync</c>), but is still rejected here to avoid
    ///     letting an operator accumulate meaningless duplicate rows.
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
    ///     This action removes a list assignment from the client group.
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
    ///     This method converts a client group list assignment entity into its REST
    ///     representation.
    /// </summary>
    private static ClientGroupListAssignmentDetail ToDetail(
        DnsClientGroupListAssignmentEntity assignment
    ) => new(assignment.Id, assignment.ClientGroupId, assignment.ListId, assignment.CreatedAtUtc);
}
