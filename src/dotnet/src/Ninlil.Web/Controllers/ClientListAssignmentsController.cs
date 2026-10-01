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
///     This controller manages which block and allow lists apply directly to one known client,
///     nested under it at <c>/api/clients/{clientId}/list-assignments</c>, as
///     <see cref="ClientGroupListAssignmentsController" /> does for a group. A client's direct
///     assignments are added to those of its matched
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity" /> (see
///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine.ResolveApplicableListsAsync</c>). Reads need
///     <c>clients:read</c> and changes need <c>clients:write</c>, as for
///     <see cref="ClientsController" />. There is no PUT or PATCH: an assignment only joins a
///     client to a list, so changing the list means deleting one assignment and creating another
///     (see <see cref="ClientListAssignmentWriteRequest" />).
/// </summary>
/// <param name="clientRepository">Confirms the client exists.</param>
/// <param name="filterListRepository">Confirms the assigned list exists.</param>
/// <param name="listAssignmentRepository">Reads and writes assignments.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
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
    ///     This action lists a client's direct list assignments, one page at a time. Returns 404
    ///     for an unknown client. Requires <c>clients:read</c>.
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
    ///     This action returns one list assignment. Returns 404 when it does not exist or belongs
    ///     to another client. Requires <c>clients:read</c>.
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
    ///     This action assigns a block or allow list to the client. Returns 400 when the list does
    ///     not exist (an invalid value in the request, not a missing resource), 404 for an unknown
    ///     client, and 409 when the list is already assigned (refused to keep rows meaningful, as
    ///     in <see cref="ClientGroupListAssignmentsController.Create" />). Requires
    ///     <c>clients:write</c>.
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
    ///     This action removes a list assignment from the client. Returns 404 when it does not
    ///     exist or belongs to another client. Requires <c>clients:write</c>.
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
    ///     This method converts an assignment entity to its API representation.
    /// </summary>
    private static ClientListAssignmentDetail ToDetail(DnsClientListAssignmentEntity assignment) =>
        new(assignment.Id, assignment.ClientId, assignment.ListId, assignment.CreatedAtUtc);
}
