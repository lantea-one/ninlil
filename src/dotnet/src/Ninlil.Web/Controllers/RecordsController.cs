using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Web.Examples.Records;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the records of one zone, nested under it at
///     <c>/api/zones/{zoneId}/records</c>. Every content change goes through
///     <see cref="IZoneUpdateService" />, the single path for changing zone content, so the SOA
///     serial, the transfer journal, the cache reload and, for a signed zone, re-signing are
///     handled there. Reads need <c>zones:read</c> and changes need <c>zones:write</c>, as for
///     <see cref="ZonesController" />.
///     <para>
///         <see cref="IZoneUpdateService" /> is shared with the DNS-protocol write paths (RFC 2136
///         dynamic update, AXFR/IXFR pulls, cloud DNS sync, CAA and PTR management, and the ACME
///         DNS-01 responder), so it reports failures as <see cref="InvalidOperationException" />
///         rather than the REST exception types. This controller catches that exception at its
///         boundary and maps it with <see cref="TranslateZoneUpdateFailure" />.
///     </para>
/// </summary>
/// <param name="zoneRepository">Confirms the zone exists.</param>
/// <param name="recordRepository">Reads records.</param>
/// <param name="zoneUpdateService">Applies every change to record content.</param>
[ApiController]
[Route("api/zones/{zoneId:guid}/records")]
public class RecordsController(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService
) : ControllerBase
{
    /// <summary>
    ///     This action lists a zone's records, one page at a time, with their data as structured
    ///     camelCase JSON. Returns 404 for an unknown zone. Requires <c>zones:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("zones:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RecordDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<RecordDetail>>> List(
        Guid zoneId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await zoneRepository.GetByIdAsync(zoneId, stoppingToken) is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // The page and the total count are both computed by the storage engine for this zone
        // (ORDER BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading the whole zone into
        // memory.
        IReadOnlyList<RecordEntity> records = await recordRepository.SearchByZoneIdAsync(
            zoneId,
            skip,
            take,
            stoppingToken
        );
        int totalCount = await recordRepository.CountByZoneIdAsync(zoneId, stoppingToken);

        return Ok(
            new PagedResult<RecordDetail>([.. records.Select(ToDetail)], skip, take, totalCount)
        );
    }

    /// <summary>
    ///     This action returns one record. Returns 404 when the record does not exist or belongs to
    ///     another zone. Requires <c>zones:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("zones:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RecordDetailExample))]
    public async Task<ActionResult<RecordDetail>> Get(
        Guid zoneId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        RecordEntity record = await recordRepository.GetByIdAsync(id, stoppingToken);
        if (record is null || record.ZoneId != zoneId)
            throw new NotFoundException("Record", id.ToString());

        return Ok(ToDetail(record));
    }

    /// <summary>
    ///     This action adds a record to the zone. A zone's first record must be its SOA, since
    ///     without it there is no serial to advance; that first record bootstraps the zone through
    ///     <see cref="IZoneUpdateService.BootstrapZoneAsync" />, and later records go through
    ///     <see cref="IZoneUpdateService.ApplyChangesAsync" />. Returns 400 when the first record
    ///     is not an SOA or the data does not fit the record type, 404 for an unknown zone, and 409
    ///     for a second SOA. Requires <c>zones:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(typeof(RecordWriteRequest), typeof(RecordWriteRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(RecordDetailExample))]
    public async Task<ActionResult<RecordDetail>> Create(
        Guid zoneId,
        RecordWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (await zoneRepository.GetByIdAsync(zoneId, stoppingToken) is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        RecordEntity record = BuildRecord(zoneId, request);

        IReadOnlyList<RecordEntity> existing = await recordRepository.ListByZoneIdAsync(
            zoneId,
            stoppingToken
        );

        if (existing.Count == 0)
        {
            if (request.Type != DnsResourceRecordTypeEnum.Soa)
                throw new ValidationException(
                    "A zone's very first record must be its own SOA record."
                );

            try
            {
                await zoneUpdateService.BootstrapZoneAsync(zoneId, [record], stoppingToken);
            }
            catch (InvalidOperationException exception)
            {
                throw TranslateZoneUpdateFailure(exception, zoneId);
            }
        }
        else
        {
            if (request.Type == DnsResourceRecordTypeEnum.Soa)
                throw new ConflictException(
                    "This zone already has an SOA record; edit it with PUT/PATCH instead of creating a second one."
                );

            try
            {
                await zoneUpdateService.ApplyChangesAsync(
                    zoneId,
                    [record],
                    [],
                    stoppingToken: stoppingToken
                );
            }
            catch (InvalidOperationException exception)
            {
                throw TranslateZoneUpdateFailure(exception, zoneId);
            }
        }

        return CreatedAtAction(nameof(Get), new { zoneId, id = record.Id }, ToDetail(record));
    }

    /// <summary>
    ///     This action replaces a record's fields, keeping its id. Returns 400 when the data does
    ///     not fit the record type and 404 when the record does not exist or belongs to another
    ///     zone, and 409 when the edit would change the type to or from SOA or rename
    ///     the SOA record (a zone has exactly one SOA). Requires <c>zones:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(typeof(RecordWriteRequest), typeof(RecordWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid zoneId,
        Guid id,
        RecordWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        RecordEntity existingRecord = await recordRepository.GetByIdAsync(id, stoppingToken);
        if (existingRecord is null || existingRecord.ZoneId != zoneId)
            throw new NotFoundException("Record", id.ToString());

        ThrowIfEditMovesSoa(existingRecord, request.Type, request.Name);

        string dataJson = RecordDataValidator.EncodeDataOrThrow(request.Type, request.Data);

        try
        {
            await zoneUpdateService.ApplyRecordEditAsync(
                zoneId,
                id,
                record =>
                {
                    record.Name = request.Name;
                    record.Type = request.Type;
                    record.Class = request.Class;
                    record.TimeToLiveSeconds = request.TimeToLiveSeconds;
                    record.DataJson = dataJson;
                    record.IsDisabled = request.IsDisabled;
                    record.Comments = request.Comments;
                    record.ExcludeFromSync = request.ExcludeFromSync;
                },
                stoppingToken
            );
        }
        catch (InvalidOperationException exception)
        {
            throw TranslateZoneUpdateFailure(exception, zoneId, id);
        }

        return NoContent();
    }

    /// <summary>
    ///     This action updates a record as an RFC 7396 merge patch: an absent field is unchanged, a
    ///     JSON <c>null</c> clears a nullable field, and any other value replaces it. Changing
    ///     <c>type</c> without new <c>data</c> re-checks the existing data against the new type and
    ///     returns 400 if it does not fit, and 409 when the patch would change the type to or
    ///     from SOA or rename the SOA record. Requires <c>zones:write</c>.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(RecordPatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid zoneId,
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        RecordEntity existingRecord = await recordRepository.GetByIdAsync(id, stoppingToken);
        if (existingRecord is null || existingRecord.ZoneId != zoneId)
            throw new NotFoundException("Record", id.ToString());

        // A patch that changes the data but not the type is checked against the record's current
        // type; one that changes both is checked against the new type.
        DnsResourceRecordTypeEnum effectiveType = existingRecord.Type;
        if (patch.TryGetMergePatchField("type", out JsonElement type))
            effectiveType = System.Text.Json.JsonSerializer.Deserialize<DnsResourceRecordTypeEnum>(
                type.GetRawText()
            );

        string patchedName = existingRecord.Name;
        if (patch.TryGetMergePatchField("name", out JsonElement patchName))
            patchedName = patchName.GetString();

        ThrowIfEditMovesSoa(existingRecord, effectiveType, patchedName);

        string newDataJson = null;
        if (patch.TryGetMergePatchField("data", out JsonElement data))
        {
            newDataJson = RecordDataValidator.EncodeDataOrThrow(effectiveType, data);
        }
        else if (effectiveType != existingRecord.Type)
        {
            // The type changed without new data, so the old data is re-checked (and re-normalized)
            // against the new type rather than kept in a shape that no longer matches.
            try
            {
                newDataJson = RecordDataValidator.EncodeDataFromJsonOrThrow(
                    effectiveType,
                    existingRecord.DataJson
                );
            }
            catch (ValidationException exception)
            {
                throw new ValidationException(
                    $"Changing this record's type to '{effectiveType}' requires also supplying 'data' in a shape that type understands: {exception.Message}"
                );
            }
        }

        try
        {
            await zoneUpdateService.ApplyRecordEditAsync(
                zoneId,
                id,
                record =>
                {
                    if (patch.TryGetMergePatchField("name", out JsonElement name))
                        record.Name = name.GetString();

                    record.Type = effectiveType;

                    if (patch.TryGetMergePatchField("class", out JsonElement dnsClass))
                        record.Class = System.Text.Json.JsonSerializer.Deserialize<DnsClassEnum>(
                            dnsClass.GetRawText()
                        );

                    if (
                        patch.TryGetMergePatchField(
                            "timeToLiveSeconds",
                            out JsonElement timeToLiveSeconds
                        )
                    )
                        record.TimeToLiveSeconds = timeToLiveSeconds.GetUInt32();

                    if (newDataJson is not null)
                        record.DataJson = newDataJson;

                    if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
                        record.IsDisabled = isDisabled.GetBoolean();

                    if (patch.TryGetMergePatchField("comments", out JsonElement comments))
                        record.Comments =
                            comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

                    if (
                        patch.TryGetMergePatchField(
                            "excludeFromSync",
                            out JsonElement excludeFromSync
                        )
                    )
                        record.ExcludeFromSync = excludeFromSync.GetBoolean();
                },
                stoppingToken
            );
        }
        catch (InvalidOperationException exception)
        {
            throw TranslateZoneUpdateFailure(exception, zoneId, id);
        }

        return NoContent();
    }

    /// <summary>
    ///     This action removes a record from the zone. The SOA record cannot be removed (409),
    ///     since every zone must keep exactly one; delete the zone instead. Requires
    ///     <c>zones:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("zones:write")]
    public async Task<IActionResult> Delete(Guid zoneId, Guid id, CancellationToken stoppingToken)
    {
        RecordEntity record = await recordRepository.GetByIdAsync(id, stoppingToken);
        if (record is null || record.ZoneId != zoneId)
            throw new NotFoundException("Record", id.ToString());

        if (record.Type == DnsResourceRecordTypeEnum.Soa)
            throw new ConflictException(
                "A zone's own SOA record cannot be deleted directly; delete the zone itself instead."
            );

        try
        {
            await zoneUpdateService.ApplyChangesAsync(
                zoneId,
                [],
                [record],
                stoppingToken: stoppingToken
            );
        }
        catch (InvalidOperationException exception)
        {
            throw TranslateZoneUpdateFailure(exception, zoneId);
        }

        return NoContent();
    }

    /// <summary>
    ///     This method builds a new, unsaved record entity from a create request, checking and
    ///     normalizing its type-specific data.
    /// </summary>
    private static RecordEntity BuildRecord(Guid zoneId, RecordWriteRequest request)
    {
        string dataJson = RecordDataValidator.EncodeDataOrThrow(request.Type, request.Data);

        return new RecordEntity
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = request.Name,
            Type = request.Type,
            Class = request.Class,
            TimeToLiveSeconds = request.TimeToLiveSeconds,
            DataJson = dataJson,
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            ExcludeFromSync = request.ExcludeFromSync,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    ///     This method throws <see cref="ConflictException" /> when an edit would change a record's
    ///     type to or from SOA, or rename the SOA record. A zone has exactly one SOA, at its apex
    ///     (RFC 1034 §4.2.1). Owner names compare case-insensitively without a trailing dot.
    /// </summary>
    private static void ThrowIfEditMovesSoa(
        RecordEntity existingRecord,
        DnsResourceRecordTypeEnum newType,
        string newName
    )
    {
        bool isSoa = existingRecord.Type == DnsResourceRecordTypeEnum.Soa;
        bool typeChanged = existingRecord.Type != newType;
        bool renamed =
            isSoa
            && !string.Equals(
                (existingRecord.Name ?? string.Empty).TrimEnd('.'),
                (newName ?? string.Empty).TrimEnd('.'),
                StringComparison.OrdinalIgnoreCase
            );

        if ((typeChanged && (isSoa || newType == DnsResourceRecordTypeEnum.Soa)) || renamed)
            throw new ConflictException(
                "A record's type cannot be changed to or from SOA, and the SOA record cannot be renamed: a zone has exactly one SOA, at its apex (RFC 1034 section 4.2.1). Edit the SOA's fields instead."
            );
    }

    /// <summary>
    ///     This method maps an <see cref="InvalidOperationException" /> from
    ///     <see cref="IZoneUpdateService" /> to a REST exception by the message wording
    ///     <see cref="ZoneUpdateService" /> uses: a missing zone or record becomes
    ///     <see cref="NotFoundException" /> and a repeated bootstrap becomes
    ///     <see cref="ConflictException" />. An unrecognized message is not guessed at; it becomes
    ///     a <see cref="NinlilException" />, which still yields a <c>ProblemDetails</c> response.
    /// </summary>
    /// <param name="exception">The exception raised by the <see cref="IZoneUpdateService" /> call.</param>
    /// <param name="zoneId">The zone the failing call was made against.</param>
    /// <param name="recordId">
    ///     The record the failing call was made against, or <see langword="null" /> when there is
    ///     none yet (such as the bootstrap branch of <see cref="Create" />).
    /// </param>
    /// <returns>The exception this controller should throw in place of <paramref name="exception" />.</returns>
    private static Exception TranslateZoneUpdateFailure(
        InvalidOperationException exception,
        Guid zoneId,
        Guid? recordId = null
    )
    {
        string message = exception.Message;

        if (message.StartsWith("No zone with id", StringComparison.Ordinal))
            return new NotFoundException("Zone", zoneId.ToString());

        if (
            message.Contains(
                "already has records; it cannot be bootstrapped again",
                StringComparison.Ordinal
            )
        )
            return new ConflictException(message);

        if (message.StartsWith("An edit cannot change", StringComparison.Ordinal))
            return new ConflictException(message);

        if (
            recordId is not null
            && message.StartsWith("No record with id", StringComparison.Ordinal)
        )
            return new NotFoundException("Record", recordId.Value.ToString());

        // Most likely a missing or corrupt SOA record: an understood internal state the caller
        // could not have avoided, so not a 400, 404 or 409.
        return new NinlilException(message, exception);
    }

    /// <summary>
    ///     This method re-serializes a record's stored <c>DataJson</c>, which is always in the
    ///     PascalCase shape <c>Ninlil.Common.Serializer.JsonSerializer</c> produces (the shape the
    ///     wire codecs, AXFR/IXFR and dynamic update use), into the camelCase the rest of the API
    ///     uses. It is done only on the way out: storing camelCase from this controller while the
    ///     other write paths store PascalCase would leave <c>RecordEntity.DataJson</c> in mixed
    ///     shapes.
    /// </summary>
    private static string ToCamelCaseDataJson(DnsResourceRecordTypeEnum type, string dataJson)
    {
        if (!DnsRecordDataCodecRegistry.TryGetCodec(type, out IDnsRecordDataCodec codec))
            return dataJson;

        object typedData = Ninlil.Common.Serializer.JsonSerializer.Deserialize(
            codec.DataType,
            dataJson
        );

        return Ninlil.Common.Serializer.JsonSerializer.Serialize(
            codec.DataType,
            typedData,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
    }

    /// <summary>
    ///     This method converts a record entity to its API representation, with its data as
    ///     structured JSON rather than an escaped string.
    /// </summary>
    private static RecordDetail ToDetail(RecordEntity record)
    {
        using JsonDocument document = JsonDocument.Parse(
            ToCamelCaseDataJson(record.Type, record.DataJson)
        );

        return new RecordDetail(
            record.Id,
            record.ZoneId,
            record.Name,
            record.Type,
            record.Class,
            record.TimeToLiveSeconds,
            document.RootElement.Clone(),
            record.IsDisabled,
            record.Comments,
            record.ExcludeFromSync,
            record.CreatedAtUtc,
            record.UpdatedAtUtc
        );
    }
}
