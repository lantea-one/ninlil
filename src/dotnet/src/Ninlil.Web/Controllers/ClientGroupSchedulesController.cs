using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientGroupSchedules;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the time-of-day parental-control schedules of one client group,
///     nested under it at <c>/api/client-groups/{groupId}/schedules</c>, as
///     <see cref="ClientSchedulesController" /> does for one client. An active group schedule
///     blocks every known client whose
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientEntity.ClientGroupId" /> is this
///     group, in addition to that client's own schedules (see
///     <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c>). Reads need
///     <c>clientgroups:read</c> and changes need <c>clientgroups:write</c>, as for
///     <see cref="ClientGroupsController" />.
/// </summary>
/// <param name="clientGroupRepository">Confirms the group exists.</param>
/// <param name="scheduleRepository">Reads and writes schedules.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
[ApiController]
[Route("api/client-groups/{groupId:guid}/schedules")]
public class ClientGroupSchedulesController(
    IDnsClientGroupRepository clientGroupRepository,
    IDnsClientGroupScheduleRepository scheduleRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists a group's schedules, one page at a time. Returns 404 for an unknown
    ///     group. Requires <c>clientgroups:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
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
    ///     This action returns one schedule. Returns 404 when it does not exist or belongs to
    ///     another group. Requires <c>clientgroups:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
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
    ///     This action adds a schedule to the group. Returns 400 for no days, an unrecognized time
    ///     zone or equal start and end times, and 404 for an unknown group. Requires
    ///     <c>clientgroups:write</c>.
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
    ///     This action replaces a schedule's fields, keeping its id and group. Validation is as for
    ///     <see cref="Create" />; returns 404 when the schedule does not exist or belongs to
    ///     another group. Requires <c>clientgroups:write</c>.
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
    ///     This action removes a schedule from the group. Returns 404 when it does not exist or
    ///     belongs to another group. Requires <c>clientgroups:write</c>.
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
    ///     This method checks a schedule request with the same rules as
    ///     <see cref="ClientSchedulesController.TryValidate" />.
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
    ///     This method converts a group schedule entity to its API representation.
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
