using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Records;

/// <summary>
///     This class supplies the Swagger examples for <see cref="RecordDetail" />, as returned by
///     <see cref="Controllers.RecordsController.Get" /> and
///     <see cref="Controllers.RecordsController.Create" />. Its 37 examples use the same keys as
///     <see cref="RecordWriteRequestExample" />, each showing the record that request creates, with
///     an identifier and timestamps.
/// </summary>
public class RecordDetailExample : IMultipleExamplesProvider<RecordDetail>
{
    /// <summary>
    ///     This method returns one <see cref="RecordDetail" /> per entry in
    ///     <see cref="RecordExampleCatalog.Entries" />, with the name, type, TTL, data and comments
    ///     the matching request submits. Names inside the data carry the trailing dot the API
    ///     writes, and enum fields use the API's camelCase form (see
    ///     <see cref="RecordExampleEntry.ResponseData" />). The SOA serial is the one submitted;
    ///     the server assigns its own serial when it stores the record.
    /// </summary>
    public IEnumerable<SwaggerExample<RecordDetail>> GetExamples() =>
        RecordExampleCatalog.Entries.Select(entry => new SwaggerExample<RecordDetail>
        {
            Name = entry.Key,
            Summary = entry.Comments,
            Value = new RecordDetail(
                Id: entry.Id,
                ZoneId: RecordExampleCatalog.ExampleZoneId,
                Name: entry.Name,
                Type: entry.Type,
                Class: DnsClassEnum.Internet,
                TimeToLiveSeconds: entry.TimeToLiveSeconds,
                Data: entry.ResponseData(),
                IsDisabled: false,
                Comments: entry.Comments,
                ExcludeFromSync: false,
                CreatedAtUtc: entry.CreatedAtUtc,
                UpdatedAtUtc: null
            ),
        });
}
