using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsCache;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DnsCacheController.List" />.
/// </summary>
public class DnsCacheDetailListExample : IExamplesProvider<IReadOnlyList<DnsCacheDetail>>
{
    /// <summary>
    ///     This method returns a home setup that forwards to a public resolver: a busy forwarded
    ///     cache, and a recursive cache holding little beyond the root priming data.
    /// </summary>
    public IReadOnlyList<DnsCacheDetail> GetExamples() =>
        [
            new(Name: "forwarded", EntryCount: 1842, MaxEntries: 10000, Hits: 51377, Misses: 9120),
            new(Name: "recursive", EntryCount: 27, MaxEntries: 100000, Hits: 12, Misses: 40),
        ];
}
