using System.Text.Json;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries every writable field of a <c>RecordEntity</c>, used to create a record
///     (<c>POST /api/zones/{zoneId}/records</c>) and to fully replace one
///     (<c>PUT /api/zones/{zoneId}/records/{id}</c>).
/// </summary>
/// <param name="Name">The record's owner name.</param>
/// <param name="Type">The record's DNS TYPE.</param>
/// <param name="TimeToLiveSeconds">The record's time-to-live, in seconds.</param>
/// <param name="Data">
///     The record's type-specific data, shaped like the record type's data model: for example
///     <c>{ "address": "203.0.113.5" }</c> for an <c>A</c> record or
///     <c>{ "preference": 10, "exchange": "mail.example.com" }</c> for an <c>MX</c> record.
/// </param>
/// <param name="Class">The record's DNS CLASS. Defaults to <c>IN</c>, the only class in practical use.</param>
/// <param name="IsDisabled">Whether the record is disabled.</param>
/// <param name="Comments">Free-form operator notes about the record.</param>
/// <param name="ExcludeFromSync">
///     Whether the record is local-only: never sent in a zone transfer or cloud push, and never
///     overwritten or deleted by an incoming one.
/// </param>
public record RecordWriteRequest(
    string Name,
    DnsResourceRecordTypeEnum Type,
    uint TimeToLiveSeconds,
    JsonElement Data,
    DnsClassEnum Class = DnsClassEnum.Internet,
    bool IsDisabled = false,
    string Comments = null,
    bool ExcludeFromSync = false
);

/// <summary>
///     This class carries a record, as returned by the list, get and write endpoints under
///     <c>/api/zones/{zoneId}/records</c>. The fields mean what they mean in
///     <see cref="RecordWriteRequest" />.
/// </summary>
public record RecordDetail(
    Guid Id,
    Guid ZoneId,
    string Name,
    DnsResourceRecordTypeEnum Type,
    DnsClassEnum Class,
    uint TimeToLiveSeconds,
    JsonElement Data,
    bool IsDisabled,
    string Comments,
    bool ExcludeFromSync,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);
