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
///     This controller serves the persistent query log <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogSubscriber" />
///     writes to for every answered query -- a real, searchable historical record, unlike the
///     dashboard's own live SignalR feed, which is capped and resets on every reload. This is a
///     flat, top-level, read-mostly resource with no parent relationship at all, matching
///     <see cref="ForwardersController" />'s/<see cref="DnsRewritesController" />'s own precedent.
///     Every search action requires the <c>querylog:read</c> permission; the one write action
///     (clearing the log outright) requires <c>querylog:write</c> instead.
///     <para>
///         Unlike this project's own much lower-cardinality entities (zones, forwarders, and so
///         on), where <see cref="PaginationQuery" />'s own in-memory <c>Skip</c>/<c>Take</c> over
///         an already-fetched full list is an accepted tradeoff, a query log can genuinely reach
///         millions of rows -- so every filter this action accepts is pushed all the way down into
///         <see cref="IDnsQueryLogRepository.SearchAsync" />/<see cref="IDnsQueryLogRepository.CountAsync" />,
///         which translate them into a real, indexed database query rather than filtering a
///         fully-materialized list.
///     </para>
/// </summary>
/// <param name="queryLogRepository">The repository this controller searches and clears the log through.</param>
[ApiController]
[Route("api/query-log")]
public class QueryLogController(IDnsQueryLogRepository queryLogRepository) : ControllerBase
{
    /// <summary>
    ///     This action searches the query log, newest entries first, narrowed by whichever of
    ///     <paramref name="clientAddress" />/<paramref name="domain" />/<paramref name="queriedType" />/
    ///     <paramref name="responseCode" />/<paramref name="fromUtc" />/<paramref name="toUtc" /> the
    ///     caller supplied.
    /// </summary>
    /// <param name="clientAddress">An exact match against a logged entry's own client address.</param>
    /// <param name="domain">A case-insensitive substring match against a logged entry's own queried name.</param>
    /// <param name="queriedType">
    ///     An exact match against a logged entry's own queried record type, given by its enum
    ///     member name (for example <c>"A"</c> or <c>"Aaaa"</c>).
    /// </param>
    /// <param name="responseCode">
    ///     An exact match against a logged entry's own response code, given by its enum member
    ///     name (for example <c>"NoError"</c> or <c>"NxDomain"</c>).
    /// </param>
    /// <param name="fromUtc">The inclusive start of the time range to search.</param>
    /// <param name="toUtc">The inclusive end of the time range to search.</param>
    /// <param name="clientIdentifier">An exact match against a logged entry's own resolved, persistent client identifier (a MAC address or DUID).</param>
    /// <param name="clientProtocolVersion">
    ///     An exact match against a logged entry's own resolved client protocol version, given by
    ///     its enum member name (for example <c>"IPv4"</c> or <c>"IPv6"</c>) -- narrows which of
    ///     the two disjoint identifier spaces <paramref name="clientIdentifier" /> must belong to.
    /// </param>
    /// <param name="pagination">The offset/limit pagination parameters this action accepts.</param>
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
    ///     This action permanently removes every entry currently in the query log -- an explicit,
    ///     operator-triggered action distinct from <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogCleanupService" />'s
    ///     own automatic, retention-driven pruning.
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
    ///     This method parses an optional query-string enum filter, treating an absent or blank
    ///     value as "no filter on this axis" rather than an error. A value is accepted whether it
    ///     is spelled as the enum's own C# member name (for example <c>"NxDomain"</c>) or as the
    ///     wire-format string a response body actually renders that same value as (for example
    ///     <c>"NXDOMAIN"</c>, via each member's own <see cref="EnumMemberAttribute" />) -- the two
    ///     differ by more than casing for several <see cref="DnsResponseCodeEnum" /> members
    ///     (<c>FormatError</c>/<c>"FORMERR"</c>, <c>ServerFailure</c>/<c>"SERVFAIL"</c>, and so
    ///     on), so a plain, case-insensitive <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />
    ///     against the member name alone would silently reject the exact value a caller just read
    ///     back out of an earlier search result for those members. A value matching neither form
    ///     is rejected outright, rather than silently falling back to the enum's own default
    ///     member -- an easy trap with a generic reflection-based enum-string helper, since a
    ///     boxed default value is not itself a C# <see langword="null" /> and can look like a
    ///     successful parse if a helper is not written carefully.
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
    ///     This method converts a query log entity into its REST representation.
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
