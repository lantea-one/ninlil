using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnsCache;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnsCacheRemovalResult" />, as
///     returned by <see cref="Controllers.DnsCacheController.Flush" /> and
///     <see cref="Controllers.DnsCacheController.RemoveName" />.
/// </summary>
public class DnsCacheRemovalResultExample : IExamplesProvider<DnsCacheRemovalResult>
{
    /// <summary>
    ///     This method returns the result of removing one name that had an A, an AAAA and an HTTPS
    ///     answer cached.
    /// </summary>
    public DnsCacheRemovalResult GetExamples() => new(RemovedEntryCount: 3);
}
