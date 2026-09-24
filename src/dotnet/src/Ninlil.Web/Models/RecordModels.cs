using System.Text.Json;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries every writable field of a <c>RecordEntity</c> -- used both to create a
///     new record (<c>POST /api/zones/{zoneId}/records</c>) and to fully replace an existing one
///     (<c>PUT /api/zones/{zoneId}/records/{id}</c>).
/// </summary>
/// <param name="Name">The record's owner name.</param>
/// <param name="Type">The record's DNS TYPE.</param>
/// <param name="TimeToLiveSeconds">The record's time-to-live, in seconds.</param>
/// <param name="Data">
///     The record's type-specific data, shaped exactly like the record type's own wire-format
///     model -- an <c>A</c> record's <c>{ "address": "203.0.113.5" }</c>, an <c>MX</c> record's
///     <c>{ "preference": 10, "exchange": "mail.example.com" }</c>, and so on.
/// </param>
/// <param name="Class">The record's DNS CLASS. Every record Ninlil serves uses <c>IN</c>.</param>
/// <param name="IsDisabled">Whether the record is currently disabled.</param>
/// <param name="Comments">Free-form operator notes about the record.</param>
/// <param name="ExcludeFromSync">Whether the record is local-only, excluded from every sync path.</param>
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
///     This class carries a record's full state, for both list and single-item responses.
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
