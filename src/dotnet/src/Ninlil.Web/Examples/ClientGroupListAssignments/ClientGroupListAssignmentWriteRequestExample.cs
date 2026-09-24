using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupListAssignments;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientGroupListAssignmentWriteRequest" />, referenced by
///     <see cref="Controllers.ClientGroupListAssignmentsController.Create" />.
/// </summary>
public class ClientGroupListAssignmentWriteRequestExample
    : IExamplesProvider<ClientGroupListAssignmentWriteRequest>
{
    /// <summary>
    ///     This method assigns the subscribed "Hagezi Pro Blocklist"
    ///     (<see cref="FilterLists.FilterListDetailExample" />'s second example) to a client
    ///     group -- there is deliberately no other field to set; changing which list is assigned
    ///     means deleting this assignment and creating another, per
    ///     <see cref="Controllers.ClientGroupListAssignmentsController" />'s own doc comment.
    /// </summary>
    public ClientGroupListAssignmentWriteRequest GetExamples() =>
        new(Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"));
}
