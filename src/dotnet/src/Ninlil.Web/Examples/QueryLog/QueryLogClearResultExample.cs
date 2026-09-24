using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.QueryLog;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="QueryLogClearResult" />, referenced by
///     <see cref="Controllers.QueryLogController.Clear" />.
/// </summary>
public class QueryLogClearResultExample : IExamplesProvider<QueryLogClearResult>
{
    /// <summary>
    ///     This method returns a plausible-sized "the log is now empty" outcome.
    /// </summary>
    public QueryLogClearResult GetExamples() => new(48213);
}
