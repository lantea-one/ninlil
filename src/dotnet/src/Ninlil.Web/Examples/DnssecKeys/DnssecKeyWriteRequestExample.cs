using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnssecKeyWriteRequest" />, referenced by
///     <see cref="Controllers.DnssecKeysController.Replace" />.
/// </summary>
public class DnssecKeyWriteRequestExample : IExamplesProvider<DnssecKeyWriteRequest>
{
    /// <summary>
    ///     This method returns a request retiring a key -- role, algorithm, and key material are
    ///     permanent once generated, so retirement status and operator notes are the only fields
    ///     this record carries at all; see <see cref="DnssecKeyWriteRequest" />'s own doc comment
    ///     for why.
    /// </summary>
    public DnssecKeyWriteRequest GetExamples() =>
        new(true, "Retired after its scheduled RFC 6781 rollover to a newer key completed.");
}
