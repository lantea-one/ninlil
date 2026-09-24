using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.QueryLog;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller surfaces one known client's own slice of the persistent query log
///     (<see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogSubscriber" />) -- a per-client activity
///     view, nested under the client itself (<c>/api/clients/{clientId}/activity</c>) the same way
///     <see cref="ClientSchedulesController" />/<see cref="ClientListAssignmentsController" />
///     already nest a client's own schedules/filter-list assignments, rather than making a caller
///     re-derive the right <see cref="QueryLogController.Search" /> filter parameters by hand.
///     <para>
///         This is a thin, read-only convenience over the exact same
///         <see cref="IDnsQueryLogRepository.SearchAsync" />/<see cref="IDnsQueryLogRepository.CountAsync" />
///         <see cref="QueryLogController" /> itself calls -- never a second query path. The real
///         work this controller does is resolving <c>clientId</c> to every one of its own current
///         addresses (<see cref="DnsClientAddressEntity" />) first, since a real device may answer
///         to more than one identifier/protocol pair at once, then filtering by each in turn --
///         mirroring <see cref="DnsClientAddressEntity" />'s own composite uniqueness per address,
///         so a MAC-shaped string that happened to coincide with an unrelated device's own DUID can
///         never leak a different device's history into this one's activity view. A device with
///         exactly one address (the common case today) is filtered with a single, fully
///         pushed-down query exactly as before this device could ever have more than one; a device
///         with several merges each address's own matching entries and re-sorts/re-paginates the
///         combined result in memory, a pragmatic tradeoff until a multi-address activity view is
///         common enough to warrant its own dedicated repository query shape.
///     </para>
///     <para>
///         Gated on <c>querylog:read</c> alone, not <c>clients:read</c> -- the data this action
///         returns genuinely is query log content, and every other way to read query log content
///         in this project (<see cref="QueryLogController.Search" />) is already gated on exactly
///         that permission; requiring a second, unrelated permission here would only create a way
///         for the two views of the same underlying data to require different access in practice.
///     </para>
/// </summary>
/// <param name="clientRepository">The repository used to confirm the owning client exists.</param>
/// <param name="clientAddressRepository">The repository used to resolve the owning client's own current addresses.</param>
/// <param name="queryLogRepository">The repository this controller searches, identically to <see cref="QueryLogController" />.</param>
[ApiController]
[Route("api/clients/{clientId:guid}/activity")]
public class ClientActivityController(
    IDnsClientRepository clientRepository,
    IDnsClientAddressRepository clientAddressRepository,
    IDnsQueryLogRepository queryLogRepository
) : ControllerBase
{
    /// <summary>
    ///     This action searches the persistent query log for entries this project's own
    ///     write-time identity resolution attributed to any of <paramref name="clientId" />'s own
    ///     current addresses -- newest first, exactly like <see cref="QueryLogController.Search" />.
    /// </summary>
    /// <param name="clientId">The known client whose own activity to search.</param>
    /// <param name="pagination">The offset/limit pagination parameters this action accepts.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpGet]
    [RequirePermission("querylog:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DnsQueryLogEntryDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<DnsQueryLogEntryDetail>>> List(
        Guid clientId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await clientRepository.GetByIdAsync(clientId, stoppingToken) is null)
            throw new NotFoundException("Client", clientId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsClientAddressEntity> addresses =
            await clientAddressRepository.ListByClientIdAsync(clientId, stoppingToken);

        // The overwhelmingly common case -- a device with exactly one address -- is filtered with
        // a single, fully pushed-down query, byte-for-byte the same shape this action always used
        // before a device could ever have more than one address.
        if (addresses.Count <= 1)
        {
            DnsQueryLogSearchCriteria singleCriteria = new(
                ClientIdentifier: addresses.Count == 1 ? addresses[0].ClientIdentifier : null,
                ClientProtocolVersion: addresses.Count == 1 ? addresses[0].ProtocolVersion : null
            );

            IReadOnlyList<DnsQueryLogEntryEntity> singleEntries =
                await queryLogRepository.SearchAsync(singleCriteria, skip, take, stoppingToken);
            int singleTotalCount = await queryLogRepository.CountAsync(
                singleCriteria,
                stoppingToken
            );

            return Ok(
                new PagedResult<DnsQueryLogEntryDetail>(
                    [.. singleEntries.Select(QueryLogController.ToDetail)],
                    skip,
                    take,
                    singleTotalCount
                )
            );
        }

        // A genuinely dual-stack (or further-merged) device: search each address's own matching
        // entries far enough to cover the requested page once merged, then combine, re-sort, and
        // re-paginate in memory -- see this controller's own doc comment for why.
        int totalCount = 0;
        List<DnsQueryLogEntryEntity> merged = [];

        foreach (DnsClientAddressEntity address in addresses)
        {
            DnsQueryLogSearchCriteria criteria = new(
                ClientIdentifier: address.ClientIdentifier,
                ClientProtocolVersion: address.ProtocolVersion
            );

            merged.AddRange(
                await queryLogRepository.SearchAsync(criteria, 0, skip + take, stoppingToken)
            );
            totalCount += await queryLogRepository.CountAsync(criteria, stoppingToken);
        }

        List<DnsQueryLogEntryEntity> page =
        [
            .. merged.OrderByDescending(entry => entry.OccurredAtUtc).Skip(skip).Take(take),
        ];

        return Ok(
            new PagedResult<DnsQueryLogEntryDetail>(
                [.. page.Select(QueryLogController.ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }
}
