using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.UsersController.PatchMine" />.
/// </summary>
public class UserProfilePatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that sets the caller's display name and phone number.
    ///     Sending a field as JSON <c>null</c>, such as <c>{"phoneNumber": null}</c>, clears it;
    ///     leaving a field out keeps it.
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
