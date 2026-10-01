using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Records;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="RecordDetail" />, as
///     returned by <see cref="Controllers.RecordsController.List" />.
/// </summary>
public class RecordDetailPagedResultExample : IExamplesProvider<PagedResult<RecordDetail>>
{
    /// <summary>
    ///     This method returns the SOA, A and MX entries of
    ///     <see cref="RecordExampleCatalog.Entries" />, roughly what a new <c>example.com</c> zone
    ///     holds, rather than all 37, which would look nothing like a real zone's record list.
    /// </summary>
    public PagedResult<RecordDetail> GetExamples()
    {
        Dictionary<string, RecordExampleEntry> entriesByKey =
            RecordExampleCatalog.Entries.ToDictionary(entry => entry.Key);

        RecordDetail[] items =
        [
            .. new[] { "SOA", "A", "MX" }.Select(key => ToDetail(entriesByKey[key])),
        ];

        return new PagedResult<RecordDetail>(items, 0, 50, items.Length);
    }

    /// <summary>
    ///     This method converts a catalog entry into the <see cref="RecordDetail" /> shape,
    ///     mirroring <see cref="RecordDetailExample" />'s own conversion exactly.
    /// </summary>
    private static RecordDetail ToDetail(RecordExampleEntry entry) =>
        new(
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
        );
}
