using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientSchedules;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the time-of-day parental-control schedules of one known client,
///     nested under it at <c>/api/clients/{clientId}/schedules</c>. A schedule has its own fields
///     (days, start and end times, time zone), so unlike the list-assignment join it also
///     supports PUT. <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c>
///     evaluates schedules while answering queries. Reads need <c>clients:read</c> and changes
///     need <c>clients:write</c>, as for <see cref="ClientsController" />.
/// </summary>
/// <param name="clientRepository">Confirms the client exists.</param>
/// <param name="scheduleRepository">Reads and writes schedules.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
[ApiController]
[Route("api/clients/{clientId:guid}/schedules")]
public class ClientSchedulesController(
    IDnsClientRepository clientRepository,
    IDnsClientScheduleRepository scheduleRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists a client's schedules, one page at a time. Returns 404 for an unknown
    ///     client. Requires <c>clients:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(ClientScheduleDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<ClientScheduleDetail>>> List(
        Guid clientId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await clientRepository.GetByIdAsync(clientId, stoppingToken) is null)
            throw new NotFoundException("Client", clientId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsClientScheduleEntity> schedules =
            await scheduleRepository.SearchByClientIdAsync(clientId, skip, take, stoppingToken);
        int totalCount = await scheduleRepository.CountByClientIdAsync(clientId, stoppingToken);

        return Ok(
            new PagedResult<ClientScheduleDetail>(
                [.. schedules.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action returns one schedule. Returns 404 when it does not exist or belongs to
    ///     another client. Requires <c>clients:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientScheduleDetailExample))]
    public async Task<ActionResult<ClientScheduleDetail>> Get(
        Guid clientId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsClientScheduleEntity schedule = await scheduleRepository.GetByIdAsync(id, stoppingToken);
        if (schedule is null || schedule.ClientId != clientId)
            throw new NotFoundException("ClientSchedule", id.ToString());

        return Ok(ToDetail(schedule));
    }

    /// <summary>
    ///     This action adds a schedule to the client. Returns 400 for no days, an unrecognized time
    ///     zone or equal start and end times, and 404 for an unknown client. Requires
    ///     <c>clients:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(
        typeof(ClientScheduleWriteRequest),
        typeof(ClientScheduleWriteRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(ClientScheduleDetailExample))]
    public async Task<ActionResult<ClientScheduleDetail>> Create(
        Guid clientId,
        ClientScheduleWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (await clientRepository.GetByIdAsync(clientId, stoppingToken) is null)
            throw new NotFoundException("Client", clientId.ToString());

        if (!TryValidate(request, out string error))
            throw new ValidationException(error);

        DnsClientScheduleEntity schedule = new()
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
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

        return CreatedAtAction(nameof(Get), new { clientId, id = schedule.Id }, ToDetail(schedule));
    }

    /// <summary>
    ///     This action replaces a schedule's fields, keeping its id and client. Validation is as
    ///     for <see cref="Create" />; returns 404 when the schedule does not exist or belongs to
    ///     another client. Requires <c>clients:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(
        typeof(ClientScheduleWriteRequest),
        typeof(ClientScheduleWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid clientId,
        Guid id,
        ClientScheduleWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsClientScheduleEntity schedule = await scheduleRepository.GetByIdAsync(id, stoppingToken);
        if (schedule is null || schedule.ClientId != clientId)
            throw new NotFoundException("ClientSchedule", id.ToString());

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
    ///     This action removes a schedule from the client. Returns 404 when it does not exist or
    ///     belongs to another client. Requires <c>clients:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clients:write")]
    public async Task<IActionResult> Delete(Guid clientId, Guid id, CancellationToken stoppingToken)
    {
        DnsClientScheduleEntity schedule = await scheduleRepository.GetByIdAsync(id, stoppingToken);
        if (schedule is null || schedule.ClientId != clientId)
            throw new NotFoundException("ClientSchedule", id.ToString());

        scheduleRepository.Remove(schedule);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method checks a schedule request: <c>daysOfWeek</c> must name at least one day,
    ///     <c>timeZoneId</c> must be a time zone this host recognizes, and <c>startTime</c> must
    ///     differ from <c>endTime</c>, since a zero-length window means nothing. An
    ///     <c>endTime</c> earlier than <c>startTime</c> is a valid window that wraps past midnight.
    ///     <see cref="ClientGroupSchedulesController" /> uses it too.
    /// </summary>
    internal static bool TryValidate(ClientScheduleWriteRequest request, out string error)
    {
        error = null;

        if (request is null)
        {
            error = "A schedule request body is required.";
            return false;
        }

        if (request.DaysOfWeek == DnsScheduleDayOfWeekEnum.None)
        {
            error = "'daysOfWeek' must name at least one day.";
            return false;
        }

        if (
            string.IsNullOrWhiteSpace(request.TimeZoneId)
            || !IsRecognizedTimeZone(request.TimeZoneId)
        )
        {
            error = $"'{request.TimeZoneId}' is not a recognized time zone id.";
            return false;
        }

        if (request.StartTime == request.EndTime)
        {
            error =
                "'startTime' and 'endTime' cannot be equal -- a zero-length window is meaningless.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method reports whether this host recognizes a time zone id, without throwing.
    /// </summary>
    internal static bool IsRecognizedTimeZone(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception exception)
            when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method converts a schedule entity to its API representation.
    /// </summary>
    private static ClientScheduleDetail ToDetail(DnsClientScheduleEntity schedule) =>
        new(
            schedule.Id,
            schedule.ClientId,
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
