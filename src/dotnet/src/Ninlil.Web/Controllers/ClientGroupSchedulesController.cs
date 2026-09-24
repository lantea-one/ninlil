using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientGroupSchedules;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers the time-of-day parental-control schedules assigned to a
///     single client group, nested under it (<c>/api/client-groups/{groupId}/schedules</c>) --
///     mirrors <see cref="ClientSchedulesController" /> exactly, keyed by client group instead of
///     a single client. Every client resolved into this group (via
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientEntity.ClientGroupId" />) is
///     blocked by an active schedule here, independently of (unioned with) its own direct
///     schedule -- see
///     <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c>. Every action
///     requires the <c>clientgroups:read</c> or <c>clientgroups:write</c> permission, the same as
///     <see cref="ClientGroupsController" />.
/// </summary>
/// <param name="clientGroupRepository">The repository used to confirm the owning group exists.</param>
/// <param name="scheduleRepository">The repository this controller reads and writes schedules through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
[ApiController]
[Route("api/client-groups/{groupId:guid}/schedules")]
public class ClientGroupSchedulesController(
    IDnsClientGroupRepository clientGroupRepository,
    IDnsClientGroupScheduleRepository scheduleRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists every schedule belonging to a client group.
    /// </summary>
    [HttpGet]
    [RequirePermission("clientgroups:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(ClientGroupScheduleDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<ClientGroupScheduleDetail>>> List(
        Guid groupId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await clientGroupRepository.GetByIdAsync(groupId, stoppingToken) is null)
            throw new NotFoundException("ClientGroup", groupId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsClientGroupScheduleEntity> schedules =
            await scheduleRepository.SearchByClientGroupIdAsync(groupId, skip, take, stoppingToken);
        int totalCount = await scheduleRepository.CountByClientGroupIdAsync(groupId, stoppingToken);

        return Ok(
            new PagedResult<ClientGroupScheduleDetail>(
                [.. schedules.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action looks up a single schedule by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("clientgroups:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientGroupScheduleDetailExample))]
    public async Task<ActionResult<ClientGroupScheduleDetail>> Get(
        Guid groupId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupScheduleEntity schedule = await scheduleRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (schedule is null || schedule.ClientGroupId != groupId)
            throw new NotFoundException("ClientGroupSchedule", id.ToString());

        return Ok(ToDetail(schedule));
    }

    /// <summary>
    ///     This action adds a new schedule to the client group.
    /// </summary>
    [HttpPost]
    [RequirePermission("clientgroups:write")]
    [SwaggerRequestExample(
        typeof(ClientGroupScheduleWriteRequest),
        typeof(ClientGroupScheduleWriteRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(ClientGroupScheduleDetailExample))]
    public async Task<ActionResult<ClientGroupScheduleDetail>> Create(
        Guid groupId,
        ClientGroupScheduleWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (await clientGroupRepository.GetByIdAsync(groupId, stoppingToken) is null)
            throw new NotFoundException("ClientGroup", groupId.ToString());

        if (!TryValidate(request, out string error))
            throw new ValidationException(error);

        DnsClientGroupScheduleEntity schedule = new()
        {
            Id = Guid.NewGuid(),
            ClientGroupId = groupId,
            DaysOfWeek = request.DaysOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            TimeZoneId = request.TimeZoneId,
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await scheduleRepository.AddAsync(schedule, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(nameof(Get), new { groupId, id = schedule.Id }, ToDetail(schedule));
    }

    /// <summary>
    ///     This action fully replaces an existing schedule's own fields, keeping its own
    ///     identifier and owning client group.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("clientgroups:write")]
    [SwaggerRequestExample(
        typeof(ClientGroupScheduleWriteRequest),
        typeof(ClientGroupScheduleWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid groupId,
        Guid id,
        ClientGroupScheduleWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupScheduleEntity schedule = await scheduleRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (schedule is null || schedule.ClientGroupId != groupId)
            throw new NotFoundException("ClientGroupSchedule", id.ToString());

        if (!TryValidate(request, out string error))
            throw new ValidationException(error);

        schedule.DaysOfWeek = request.DaysOfWeek;
        schedule.StartTime = request.StartTime;
        schedule.EndTime = request.EndTime;
        schedule.TimeZoneId = request.TimeZoneId;
        schedule.IsDisabled = request.IsDisabled;
        schedule.Comments = request.Comments;
        schedule.UpdatedAtUtc = DateTimeOffset.UtcNow;

        scheduleRepository.Update(schedule);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action removes a schedule from the client group.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clientgroups:write")]
    public async Task<IActionResult> Delete(Guid groupId, Guid id, CancellationToken stoppingToken)
    {
        DnsClientGroupScheduleEntity schedule = await scheduleRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (schedule is null || schedule.ClientGroupId != groupId)
            throw new NotFoundException("ClientGroupSchedule", id.ToString());

        scheduleRepository.Remove(schedule);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method validates a schedule write request's own fields -- see
    ///     <see cref="ClientSchedulesController.TryValidate" />'s own doc comment for the full
    ///     reasoning, identical here.
    /// </summary>
    private static bool TryValidate(ClientGroupScheduleWriteRequest request, out string error)
    {
        error = null;

        if (request is null)
        {
            error = "A schedule request body is required.";
            return false;
        }

        return ClientSchedulesController.TryValidate(
            new ClientScheduleWriteRequest(
                request.DaysOfWeek,
                request.StartTime,
                request.EndTime,
                request.TimeZoneId,
                request.IsDisabled,
                request.Comments
            ),
            out error
        );
    }

    /// <summary>
    ///     This method converts a client group schedule entity into its REST representation.
    /// </summary>
    private static ClientGroupScheduleDetail ToDetail(DnsClientGroupScheduleEntity schedule) =>
        new(
            schedule.Id,
            schedule.ClientGroupId,
            schedule.DaysOfWeek,
            schedule.StartTime,
            schedule.EndTime,
            schedule.TimeZoneId,
            schedule.IsDisabled,
            schedule.Comments,
            schedule.CreatedAtUtc,
            schedule.UpdatedAtUtc
        );
}
