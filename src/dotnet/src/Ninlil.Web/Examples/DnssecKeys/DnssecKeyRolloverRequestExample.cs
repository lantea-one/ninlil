using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnssecKeyRolloverRequest" />, the
///     request body of <see cref="Controllers.DnssecKeysController.Roll" />.
/// </summary>
public class DnssecKeyRolloverRequestExample : IExamplesProvider<DnssecKeyRolloverRequest>
{
    /// <summary>
    ///     This method returns the usual request: no fields, so the new key keeps the current key's
    ///     algorithm.
    /// </summary>
    public DnssecKeyRolloverRequest GetExamples() => new();
}
