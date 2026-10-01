using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Records;

/// <summary>
///     This class supplies the Swagger examples for <see cref="RecordWriteRequest" />, the request
///     body of <see cref="Controllers.RecordsController.Create" /> and
///     <see cref="Controllers.RecordsController.Replace" />. There is one example for each of the
///     37 record types <c>Ninlil.Dns.Common.Wire.Records.DnsRecordDataCodecRegistry</c> has a codec
///     for, keyed by the type's mnemonic (<c>"A"</c>, <c>"TLSA"</c> and so on).
/// </summary>
public class RecordWriteRequestExample : IMultipleExamplesProvider<RecordWriteRequest>
{
    /// <summary>
    ///     This method returns one <see cref="RecordWriteRequest" /> per entry in
    ///     <see cref="RecordExampleCatalog.Entries" />; <see cref="RecordDetailExample" /> shows
    ///     the record each one creates.
    /// </summary>
    public IEnumerable<SwaggerExample<RecordWriteRequest>> GetExamples() =>
        RecordExampleCatalog.Entries.Select(entry => new SwaggerExample<RecordWriteRequest>
        {
            Name = entry.Key,
            Summary = entry.Comments,
            Value = new RecordWriteRequest(
                Name: entry.Name,
                Type: entry.Type,
                TimeToLiveSeconds: entry.TimeToLiveSeconds,
                Data: entry.Data,
                Comments: entry.Comments
            ),
        });
}
