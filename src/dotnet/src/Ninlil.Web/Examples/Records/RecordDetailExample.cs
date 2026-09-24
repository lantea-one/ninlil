using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Records;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="RecordDetail" />, referenced by <see cref="Controllers.RecordsController.Get" />
///     and <see cref="Controllers.RecordsController.Create" />. Every one of these 37 examples,
///     keyed identically to <see cref="RecordWriteRequestExample" />'s own 37 keys, shows the
///     exact record that request would have created, now carrying its own identifier and
///     timestamps.
/// </summary>
public class RecordDetailExample : IMultipleExamplesProvider<RecordDetail>
{
    /// <summary>
    ///     This method returns one <see cref="RecordDetail" /> per entry in
    ///     <see cref="RecordExampleCatalog.Entries" />, sharing the exact same
    ///     name/type/time-to-live/data/comments <see cref="RecordWriteRequestExample" />'s own
    ///     matching key submits.
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
                Data: entry.Data,
                IsDisabled: false,
                Comments: entry.Comments,
                ExcludeFromSync: false,
                CreatedAtUtc: entry.CreatedAtUtc,
                UpdatedAtUtc: null
            ),
        });
}
