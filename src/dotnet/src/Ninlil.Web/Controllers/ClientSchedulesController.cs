using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientSchedules;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers the time-of-day parental-control schedules assigned directly
///     to a single known client, nested under it
///     (<c>/api/clients/{clientId}/schedules</c>) since a schedule only ever makes sense in its
///     owning client's own context -- the same nesting <see cref="ClientListAssignmentsController" />
///     already establishes for a client's own filter-list assignments. Unlike that controller's
///     own pure-join resource, a schedule has real, mutable fields of its own (its days/times/
///     time zone), so this controller also supports <c>PUT</c>, mirroring
///     <see cref="DhcpReservationsController" />'s own "real child resource with its own fields"
///     shape rather than <see cref="ClientListAssignmentsController" />'s thinner one. Every
///     action requires the <c>clients:read</c> or <c>clients:write</c> permission, the same as
///     <see cref="ClientsController" />. See
///     <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c> for the real
///     evaluation logic that consumes this resource at query-answering time.
/// </summary>
/// <param name="clientRepository">The repository used to confirm the owning client exists.</param>
/// <param name="scheduleRepository">The repository this controller reads and writes schedules through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
[ApiController]
[Route("api/clients/{clientId:guid}/schedules")]
public class ClientSchedulesController(
    IDnsClientRepository clientRepository,
    IDnsClientScheduleRepository scheduleRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists every schedule belonging to a known client.
    /// </summary>
    [HttpGet]
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
    ///     This action looks up a single schedule by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action adds a new schedule to the known client.
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
    ///     This action fully replaces an existing schedule's own fields, keeping its own
    ///     identifier and owning client.
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
    ///     This action removes a schedule from the known client.
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
    ///     This method validates a schedule write request's own fields: <c>daysOfWeek</c> must
    ///     name at least one real day (a schedule matching no day at all is a real user error),
    ///     <c>timeZoneId</c> must be a real, recognized IANA time zone id, and <c>startTime</c>
    ///     must differ from <c>endTime</c> (a zero-length window, either way around the clock, is
    ///     meaningless) -- <c>endTime</c> earlier than <c>startTime</c> is otherwise a real,
    ///     accepted midnight-wrapping window, never rejected on its own.
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
    ///     This method checks whether a string is a real, recognized IANA time zone identifier on
    ///     this host, without throwing.
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
    ///     This method converts a client schedule entity into its REST representation.
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
