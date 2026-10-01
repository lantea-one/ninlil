using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
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
///     This controller serves the persistent query log that
///     <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogSubscriber" /> writes for every answered
///     query: a searchable history, unlike the dashboard's live SignalR feed, which is capped and
///     resets on reload. The route is flat. Searching needs <c>querylog:read</c>; clearing the log
///     needs <c>querylog:write</c>.
///     <para>
///         A query log can reach millions of rows, so every filter, the ordering and the page are
///         applied by <see cref="IDnsQueryLogRepository.SearchAsync" /> and
///         <see cref="IDnsQueryLogRepository.CountAsync" /> as an indexed database query.
///     </para>
/// </summary>
/// <param name="queryLogRepository">Searches and clears the log.</param>
[ApiController]
[Route("api/query-log")]
public class QueryLogController(IDnsQueryLogRepository queryLogRepository) : ControllerBase
{
    /// <summary>
    ///     This action searches the query log, newest first, one page at a time, narrowed by any of
    ///     the filters given. Returns 400 for an unknown record type, response code or protocol
    ///     version. Requires <c>querylog:read</c>.
    /// </summary>
    /// <param name="clientAddress">An exact match on the client address.</param>
    /// <param name="domain">A case-insensitive substring match on the queried name.</param>
    /// <param name="queriedType">
    ///     An exact match on the queried record type, by enum member name (for example <c>"A"</c>
    ///     or <c>"Aaaa"</c>) or by the name the API returns.
    /// </param>
    /// <param name="responseCode">
    ///     An exact match on the response code, by enum member name (for example <c>"NxDomain"</c>)
    ///     or by the name the API returns (for example <c>"NXDOMAIN"</c>).
    /// </param>
    /// <param name="fromUtc">The inclusive start of the time range to search.</param>
    /// <param name="toUtc">The inclusive end of the time range to search.</param>
    /// <param name="clientIdentifier">An exact match on the resolved, persistent client identifier (a MAC address or DUID).</param>
    /// <param name="clientProtocolVersion">
    ///     An exact match on the resolved client's protocol version (for example <c>"IPv4"</c> or
    ///     <c>"IPv6"</c>), which picks which of the two identifier spaces
    ///     <paramref name="clientIdentifier" /> belongs to.
    /// </param>
    /// <param name="pagination">The offset and limit of the page to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpGet]
    [RequirePermission("querylog:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DnsQueryLogEntryDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<DnsQueryLogEntryDetail>>> Search(
        [FromQuery] string clientAddress,
        [FromQuery] string domain,
        [FromQuery] string queriedType,
        [FromQuery] string responseCode,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] string clientIdentifier,
        [FromQuery] string clientProtocolVersion,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (
            !TryParseOptionalEnum(
                queriedType,
                out DnsResourceRecordTypeEnum? parsedType,
                out string typeError
            )
        )
            throw new ValidationException(typeError);

        if (
            !TryParseOptionalEnum(
                responseCode,
                out DnsResponseCodeEnum? parsedResponseCode,
                out string responseCodeError
            )
        )
            throw new ValidationException(responseCodeError);

        if (
            !TryParseOptionalEnum(
                clientProtocolVersion,
                out InternetProtocolVersionEnum? parsedClientProtocolVersion,
                out string clientProtocolVersionError
            )
        )
            throw new ValidationException(clientProtocolVersionError);

        DnsQueryLogSearchCriteria criteria = new(
            string.IsNullOrWhiteSpace(clientAddress) ? null : clientAddress,
            string.IsNullOrWhiteSpace(domain) ? null : domain,
            parsedType,
            parsedResponseCode,
            fromUtc,
            toUtc,
            string.IsNullOrWhiteSpace(clientIdentifier) ? null : clientIdentifier,
            parsedClientProtocolVersion
        );

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await queryLogRepository.SearchAsync(
            criteria,
            skip,
            take,
            stoppingToken
        );
        int totalCount = await queryLogRepository.CountAsync(criteria, stoppingToken);

        return Ok(
            new PagedResult<DnsQueryLogEntryDetail>(
                [.. entries.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action deletes every entry in the query log and returns how many were removed. It
    ///     is separate from the retention-based pruning of
    ///     <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogCleanupService" />. Requires
    ///     <c>querylog:write</c>.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpDelete]
    [RequirePermission("querylog:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(QueryLogClearResultExample))]
    public async Task<ActionResult<QueryLogClearResult>> Clear(CancellationToken stoppingToken)
    {
        int deletedCount = await queryLogRepository.ClearAsync(stoppingToken);
        return Ok(new QueryLogClearResult(deletedCount));
    }

    /// <summary>
    ///     This method parses an optional enum filter; an absent or blank value means no filter. A
    ///     value may be the C# member name (for example <c>"NxDomain"</c>) or the string a response
    ///     shows for it through its <see cref="EnumMemberAttribute" /> (for example
    ///     <c>"NXDOMAIN"</c>), both case-insensitive. For several
    ///     <see cref="DnsResponseCodeEnum" /> members the two differ by more than case
    ///     (<c>FormatError</c>/<c>"FORMERR"</c>, <c>ServerFailure</c>/<c>"SERVFAIL"</c>), and a
    ///     caller should be able to send back a value it just read. A value matching neither is
    ///     rejected rather than taken as the enum's default member.
    /// </summary>
    private static bool TryParseOptionalEnum<TEnum>(
        string value,
        out TEnum? parsed,
        out string error
    )
        where TEnum : struct, Enum
    {
        error = null;
        parsed = null;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (Enum.TryParse(value, true, out TEnum byMemberName))
        {
            parsed = byMemberName;
            return true;
        }

        foreach (
            FieldInfo field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static)
        )
        {
            string wireValue = field.GetCustomAttribute<EnumMemberAttribute>()?.Value;
            if (
                wireValue is null
                || !string.Equals(wireValue, value, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            parsed = (TEnum)field.GetValue(null)!;
            return true;
        }

        error = $"'{value}' is not a valid {typeof(TEnum).Name} value.";
        return false;
    }

    /// <summary>
    ///     This method converts a query log entity to its API representation.
    /// </summary>
    internal static DnsQueryLogEntryDetail ToDetail(DnsQueryLogEntryEntity entry) =>
        new(
            entry.Id,
            entry.OccurredAtUtc,
            entry.ClientAddress,
            entry.Protocol,
            entry.QueriedName,
            entry.QueriedType,
            entry.ResponseCode,
            entry.AnswerCount,
            entry.ClientIdentifier,
            entry.ClientProtocolVersion
        );
}
