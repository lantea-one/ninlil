using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnssecKeyWriteRequest" />, the
///     request body of <see cref="Controllers.DnssecKeysController.Replace" />.
/// </summary>
public class DnssecKeyWriteRequestExample : IExamplesProvider<DnssecKeyWriteRequest>
{
    /// <summary>
    ///     This method returns a request that retires a key with a note. The retirement flag and
    ///     comments are the only writable fields (see <see cref="DnssecKeyWriteRequest" />).
    /// </summary>
    public DnssecKeyWriteRequest GetExamples() =>
        new(true, "Retired after its scheduled RFC 6781 rollover to a newer key completed.");
}
