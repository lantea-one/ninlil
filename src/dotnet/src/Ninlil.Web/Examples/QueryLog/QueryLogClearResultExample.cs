using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.QueryLog;

/// <summary>
///     This class supplies the Swagger example for <see cref="QueryLogClearResult" />, as returned
///     by <see cref="Controllers.QueryLogController.Clear" />.
/// </summary>
public class QueryLogClearResultExample : IExamplesProvider<QueryLogClearResult>
{
    /// <summary>
    ///     This method returns the number of entries removed by clearing the log.
    /// </summary>
    public QueryLogClearResult GetExamples() => new(48213);
}
