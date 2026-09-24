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
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers the records stored inside a single zone, nested under it
///     (<c>/api/zones/{zoneId}/records</c>) since a record only ever makes sense in its owning
///     zone's own context. Every content change goes through <see cref="IZoneUpdateService" /> --
///     the one path this project expects a zone's record content to ever change through -- so the
///     zone's SOA serial, transfer journal, cache reload, and (for a signed zone) re-signing all
///     stay correct with no extra work in this controller. Every action requires the
///     <c>zones:read</c> or <c>zones:write</c> permission, the same as <see cref="ZonesController" />.
///     <para>
///         <see cref="IZoneUpdateService" /> itself is shared with every genuinely DNS-protocol-
///         triggered write path (RFC 2136 dynamic update, an AXFR/IXFR pull, cloud DNS sync, CAA/
///         PTR auto-management, and ACME's own DNS-01 challenge responder), so it stays completely
///         untouched by this project's own custom exception hierarchy -- converting its own
///         <see cref="InvalidOperationException" /> throws directly would change error signaling
///         on a live query/protocol response path this hierarchy is never allowed to reach.
///         Instead, this controller catches that exception right at its own REST boundary and
///         translates it, via <see cref="TranslateZoneUpdateFailure" />, into whichever of this
///         project's own custom exception types actually matches the failure -- every other write
///         path keeps seeing exactly the same <see cref="InvalidOperationException" /> it always
///         has.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to confirm the owning zone exists.</param>
/// <param name="recordRepository">The repository this controller reads records through directly.</param>
/// <param name="zoneUpdateService">The service every record content change is applied through.</param>
[ApiController]
[Route("api/zones/{zoneId:guid}/records")]
public class RecordsController(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService
) : ControllerBase
{
    /// <summary>
    ///     This action lists every record stored in a zone.
    /// </summary>
    [HttpGet]
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

        // Both the page itself and its total count are pushed down to the storage engine --
        // a real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) scoped to this
        // zone -- rather than materializing every record in the zone into memory first, so a
        // high-record-count zone never forces its whole content into memory just to answer one
        // page's worth of a request.
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
    ///     This action looks up a single record by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action adds a new record to the zone. A zone's very first record must be its own
    ///     SOA record -- there is otherwise no serial to advance -- so this bootstraps the zone
    ///     via <see cref="IZoneUpdateService.BootstrapZoneAsync" /> exactly once; every later
    ///     addition goes through the ordinary <see cref="IZoneUpdateService.ApplyChangesAsync" />
    ///     path instead.
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
    ///     This action fully replaces an existing record's fields, keeping its own identifier.
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
    ///     This action partially updates an existing record via RFC 7396 JSON Merge Patch: a
    ///     field absent from the patch document is left untouched, a field present with a JSON
    ///     <c>null</c> clears it (where the underlying field is itself nullable), and any other
    ///     present field overwrites it.
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

        // A patch that changes Data but not Type must still validate against this record's own,
        // unchanged type; one that changes both validates against the new type instead.
        DnsResourceRecordTypeEnum effectiveType = existingRecord.Type;
        if (patch.TryGetMergePatchField("type", out JsonElement type))
            effectiveType = System.Text.Json.JsonSerializer.Deserialize<DnsResourceRecordTypeEnum>(
                type.GetRawText()
            );

        string newDataJson = null;
        if (patch.TryGetMergePatchField("data", out JsonElement data))
        {
            newDataJson = RecordDataValidator.EncodeDataOrThrow(effectiveType, data);
        }
        else if (effectiveType != existingRecord.Type)
        {
            // The type changed but no new data was supplied -- the old RDATA cannot possibly
            // match the new type's own shape, so re-validate (and re-normalize) it against the
            // new type's codec rather than silently keeping data that no longer matches Type.
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
    ///     This action removes a record from the zone. A zone's own SOA record cannot be removed
    ///     this way -- every zone must always have exactly one; delete the zone itself instead.
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
    ///     This method builds a brand-new, not-yet-persisted record entity from a create request,
    ///     validating and normalizing its type-specific data along the way.
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
    ///     This method classifies an <see cref="InvalidOperationException" /> raised by
    ///     <see cref="IZoneUpdateService" /> -- a service this controller shares with every
    ///     DNS-protocol-triggered write path, and therefore never converts to throw this
    ///     project's own custom exception hierarchy directly -- into whichever of this project's
    ///     own exception types actually matches the failure, based on the specific, already-known
    ///     wording <see cref="ZoneUpdateService" /> itself uses for each condition. A message this
    ///     method does not recognize is deliberately never guessed at: it is wrapped as
    ///     <see cref="NinlilException" /> instead, so it still becomes a clean, specific
    ///     <c>ProblemDetails</c> response rather than the framework's own bare, generic
    ///     unhandled-exception fallback, without this controller pretending to know a cause it
    ///     does not actually recognize.
    /// </summary>
    /// <param name="exception">The exception raised by the <see cref="IZoneUpdateService" /> call.</param>
    /// <param name="zoneId">The zone identifier the failing call was made against.</param>
    /// <param name="recordId">
    ///     The record identifier the failing call was made against, when the call site names one
    ///     -- <see langword="null" /> for a call (such as <see cref="Create" />'s own bootstrap
    ///     branch) that has no single record identifier of its own yet.
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

        if (
            recordId is not null
            && message.StartsWith("No record with id", StringComparison.Ordinal)
        )
            return new NotFoundException("Record", recordId.Value.ToString());

        // A genuinely unexpected internal state (a missing or corrupt SOA record, most likely) --
        // not something this caller's own request could realistically have avoided, so it is
        // named and understood, but not a 404/409/400 from the caller's own perspective.
        return new NinlilException(message, exception);
    }

    /// <summary>
    ///     This method re-serializes a record's own stored <c>DataJson</c> -- always in the
    ///     PascalCase shape <c>Ninlil.Common.Serializer.JsonSerializer</c> produces by
    ///     default, matching every other internal consumer of a record's data (the wire-format
    ///     codecs, AXFR/IXFR, dynamic update) -- into the camelCase shape the rest of this REST
    ///     API already uses everywhere else. Deliberately done only at this API boundary, on the
    ///     way out, rather than changing what gets stored: storing camelCase JSON from this
    ///     controller while every other write path keeps storing PascalCase would leave
    ///     <c>RecordEntity.DataJson</c> in an inconsistent shape depending on which path last
    ///     touched a given row.
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
    ///     This method converts a record entity into its REST representation, parsing its own
    ///     stored <c>DataJson</c> back into structured JSON rather than leaving it as an escaped
    ///     string.
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
