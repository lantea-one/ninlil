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
///     This controller shows one known client's part of the persistent query log
///     (<see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogSubscriber" />), nested under the client
///     at <c>/api/clients/{clientId}/activity</c>, so a caller need not work out the
///     <see cref="QueryLogController.Search" /> filters itself.
///     <para>
///         It reads through the same <see cref="IDnsQueryLogRepository.SearchAsync" /> and
///         <see cref="IDnsQueryLogRepository.CountAsync" /> as <see cref="QueryLogController" />.
///         It first loads the client's current addresses (<see cref="DnsClientAddressEntity" />),
///         since a device may answer to several identifier and protocol pairs, then filters by
///         each pair, so an identifier string that happens to equal another device's DUID never
///         brings in that device's history. A device with one address is searched with a single
///         query in storage. For several, each address's entries up to the end of the requested
///         page are loaded and merged, sorted and paged in memory.
///     </para>
///     <para>
///         It needs <c>querylog:read</c>, not <c>clients:read</c>: what it returns is query log
///         content, which <see cref="QueryLogController.Search" /> also guards with
///         <c>querylog:read</c>, so both views of the same data need the same access.
///     </para>
/// </summary>
/// <param name="clientRepository">Confirms the client exists.</param>
/// <param name="clientAddressRepository">Loads the client's current addresses.</param>
/// <param name="queryLogRepository">Searches the query log, as <see cref="QueryLogController" /> does.</param>
[ApiController]
[Route("api/clients/{clientId:guid}/activity")]
public class ClientActivityController(
    IDnsClientRepository clientRepository,
    IDnsClientAddressRepository clientAddressRepository,
    IDnsQueryLogRepository queryLogRepository
) : ControllerBase
{
    /// <summary>
    ///     This action lists the query log entries attributed, when they were logged, to any of the
    ///     client's current addresses, newest first, one page at a time. Returns 404 for an unknown
    ///     client. Requires <c>querylog:read</c>.
    /// </summary>
    /// <param name="clientId">The known client whose activity to list.</param>
    /// <param name="pagination">The offset and limit of the page to return.</param>
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

        // The usual case, a device with one address, is a single query run in storage.
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

        // A device with several addresses (dual-stack or merged): load each address's entries up to
        // the end of the requested page, then merge, sort and page in memory.
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
