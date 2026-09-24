using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Records;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="RecordDetail" />, referenced by <see cref="Controllers.RecordsController.List" />.
/// </summary>
public class RecordDetailPagedResultExample : IExamplesProvider<PagedResult<RecordDetail>>
{
    /// <summary>
    ///     This method returns one page of the small, realistic subset of
    ///     <see cref="RecordExampleCatalog.Entries" /> (its own <c>SOA</c>/<c>A</c>/<c>MX</c>
    ///     entries) an actual, freshly-bootstrapped <c>example.com</c> zone would carry, in the
    ///     exact envelope a real list response uses -- rather than all 37, which would make this
    ///     one example far less representative of what a real zone's own record list actually
    ///     looks like.
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
