using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Records;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="RecordWriteRequest" />, referenced by
///     <see cref="Controllers.RecordsController.Create" /> and
///     <see cref="Controllers.RecordsController.Replace" />. Names one example per record type
///     <c>Ninlil.Dns.Common.Wire.Records.DnsRecordDataCodecRegistry</c> registers a real codec
///     for -- 37 in total -- each keyed by that type's own short wire name (<c>"A"</c>,
///     <c>"TLSA"</c>, and so on), so an operator already familiar with a real zone file or this
///     project's own Angular admin SPA recognizes the example they want immediately.
/// </summary>
public class RecordWriteRequestExample : IMultipleExamplesProvider<RecordWriteRequest>
{
    /// <summary>
    ///     This method returns one <see cref="RecordWriteRequest" /> per entry in
    ///     <see cref="RecordExampleCatalog.Entries" /> -- see <see cref="RecordDetailExample" />
    ///     for the matching response shape each of these same 37 keys produces once created.
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
