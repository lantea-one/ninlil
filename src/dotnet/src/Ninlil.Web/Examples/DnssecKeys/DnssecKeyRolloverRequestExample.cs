using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnssecKeyRolloverRequest" />, referenced by
///     <see cref="Controllers.DnssecKeysController.Roll" />.
/// </summary>
public class DnssecKeyRolloverRequestExample : IExamplesProvider<DnssecKeyRolloverRequest>
{
    /// <summary>
    ///     This method returns the ordinary case: no algorithm override at all, reusing whichever
    ///     algorithm the key being rolled already uses.
    /// </summary>
    public DnssecKeyRolloverRequest GetExamples() => new();
}
