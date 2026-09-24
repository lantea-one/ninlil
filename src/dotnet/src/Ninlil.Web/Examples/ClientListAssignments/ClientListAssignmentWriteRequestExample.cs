using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientListAssignments;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientListAssignmentWriteRequest" />, referenced by
///     <see cref="Controllers.ClientListAssignmentsController.Create" />.
/// </summary>
public class ClientListAssignmentWriteRequestExample
    : IExamplesProvider<ClientListAssignmentWriteRequest>
{
    /// <summary>
    ///     This method assigns the subscribed "Hagezi Pro Blocklist"
    ///     (<see cref="FilterLists.FilterListDetailExample" />'s second example) directly to a
    ///     known client -- there is deliberately no other field to set; changing which list is
    ///     assigned means deleting this assignment and creating another, per
    ///     <see cref="Controllers.ClientListAssignmentsController" />'s own doc comment.
    /// </summary>
    public ClientListAssignmentWriteRequest GetExamples() =>
        new(Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"));
}
