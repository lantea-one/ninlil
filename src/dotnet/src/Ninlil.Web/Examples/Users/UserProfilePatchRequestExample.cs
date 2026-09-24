using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.UsersController.PatchMine" />'s own RFC 7396 JSON Merge Patch
///     request body.
/// </summary>
public class UserProfilePatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that sets both the caller's own display name and phone
    ///     number. Sending a field with a JSON <c>null</c> value instead -- for example
    ///     <c>{"phoneNumber": null}</c> -- clears that field back to <see langword="null" />;
    ///     omitting a field entirely leaves it untouched.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "displayName": "Jamie Smith",
                  "phoneNumber": "+1-555-0100"
                }
                """
            )
            .RootElement.Clone();
}
