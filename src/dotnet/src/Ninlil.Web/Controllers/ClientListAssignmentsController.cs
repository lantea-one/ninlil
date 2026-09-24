using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientListAssignments;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers which block/allow lists apply directly to a single known
///     client, nested under it (<c>/api/clients/{clientId}/list-assignments</c>) since an
///     assignment only ever makes sense in its owning client's own context -- the same nesting
///     <see cref="ClientGroupListAssignmentsController" /> already establishes for a client
///     group's own assigned lists. A client's own direct assignments here are unioned with
///     whatever its matched <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity" />
///     already contributes -- see <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine.ResolveApplicableListsAsync</c>
///     for the real resolution logic that consumes this table. Every action requires the
///     <c>clients:read</c> or <c>clients:write</c> permission, the same as
///     <see cref="ClientsController" />. There is deliberately no <c>PUT</c>/<c>PATCH</c> here --
///     an assignment is just a join between a client and a list with no mutable field of its own;
///     changing which list is assigned means deleting one assignment and creating another, exactly
///     like <see cref="ClientListAssignmentWriteRequest" /> already documents.
/// </summary>
/// <param name="clientRepository">The repository used to confirm the owning client exists.</param>
/// <param name="filterListRepository">The repository used to confirm the assigned list actually exists.</param>
/// <param name="listAssignmentRepository">The repository this controller reads and writes assignments through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
[ApiController]
[Route("api/clients/{clientId:guid}/list-assignments")]
public class ClientListAssignmentsController(
    IDnsClientRepository clientRepository,
    IDnsFilterListRepository filterListRepository,
    IDnsClientListAssignmentRepository listAssignmentRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists every list assignment belonging to a known client.
    /// </summary>
    [HttpGet]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(ClientListAssignmentDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<ClientListAssignmentDetail>>> List(
        Guid clientId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await clientRepository.GetByIdAsync(clientId, stoppingToken) is null)
            throw new NotFoundException("Client", clientId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsClientListAssignmentEntity> assignments =
            await listAssignmentRepository.SearchByClientIdAsync(
                clientId,
                skip,
                take,
                stoppingToken
            );
        int totalCount = await listAssignmentRepository.CountByClientIdAsync(
            clientId,
            stoppingToken
        );

        return Ok(
            new PagedResult<ClientListAssignmentDetail>(
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
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientListAssignmentDetailExample))]
    public async Task<ActionResult<ClientListAssignmentDetail>> Get(
        Guid clientId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsClientListAssignmentEntity assignment = await listAssignmentRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (assignment is null || assignment.ClientId != clientId)
            throw new NotFoundException("ClientListAssignment", id.ToString());

        return Ok(ToDetail(assignment));
    }

    /// <summary>
    ///     This action assigns a block/allow list to the known client. Rejected with
    ///     <c>400 Bad Request</c> if the referenced list does not exist -- treated as an invalid
    ///     value on the assignment being created, not a lookup of the list itself, so this stays a
    ///     <see cref="ValidationException" /> rather than a <see cref="NotFoundException" /> -- and
    ///     <c>409 Conflict</c> if this exact client/list pairing is already assigned -- an
    ///     unenforced duplicate would be harmless to <c>DnsFilterEngine</c> itself (it collects
    ///     assigned list identifiers into a <see cref="HashSet{T}" /> before resolving them, see
    ///     <c>DnsFilterEngine.ResolveApplicableListsAsync</c>), but is still rejected here to avoid
    ///     letting an operator accumulate meaningless duplicate rows -- the same reasoning
    ///     <see cref="ClientGroupListAssignmentsController.Create" /> already documents.
    /// </summary>
    [HttpPost]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(
        typeof(ClientListAssignmentWriteRequest),
        typeof(ClientListAssignmentWriteRequestExample)
    )]
    [SwaggerResponseExample(
        StatusCodes.Status201Created,
        typeof(ClientListAssignmentDetailExample)
    )]
    public async Task<ActionResult<ClientListAssignmentDetail>> Create(
        Guid clientId,
        ClientListAssignmentWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (await clientRepository.GetByIdAsync(clientId, stoppingToken) is null)
            throw new NotFoundException("Client", clientId.ToString());

        if (request is null || request.ListId == Guid.Empty)
            throw new ValidationException("'listId' is required.");

        if (await filterListRepository.GetByIdAsync(request.ListId, stoppingToken) is null)
            throw new ValidationException($"No filter list with id '{request.ListId}' exists.");

        IReadOnlyList<DnsClientListAssignmentEntity> existing =
            await listAssignmentRepository.ListByClientIdAsync(clientId, stoppingToken);
        if (existing.Any(assignment => assignment.ListId == request.ListId))
            throw new ConflictException(
                $"List '{request.ListId}' is already assigned to this client."
            );

        DnsClientListAssignmentEntity newAssignment = new()
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ListId = request.ListId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await listAssignmentRepository.AddAsync(newAssignment, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(
            nameof(Get),
            new { clientId, id = newAssignment.Id },
            ToDetail(newAssignment)
        );
    }

    /// <summary>
    ///     This action removes a list assignment from the known client.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clients:write")]
    public async Task<IActionResult> Delete(Guid clientId, Guid id, CancellationToken stoppingToken)
    {
        DnsClientListAssignmentEntity assignment = await listAssignmentRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (assignment is null || assignment.ClientId != clientId)
            throw new NotFoundException("ClientListAssignment", id.ToString());

        listAssignmentRepository.Remove(assignment);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method converts a client list assignment entity into its REST representation.
    /// </summary>
    private static ClientListAssignmentDetail ToDetail(DnsClientListAssignmentEntity assignment) =>
        new(assignment.Id, assignment.ClientId, assignment.ListId, assignment.CreatedAtUtc);
}
