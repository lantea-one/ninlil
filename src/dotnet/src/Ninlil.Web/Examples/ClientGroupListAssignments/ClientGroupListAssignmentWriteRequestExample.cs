using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupListAssignments;

/// <summary>
///     This class supplies the Swagger example for
///     <see cref="ClientGroupListAssignmentWriteRequest" />, the request body of
///     <see cref="Controllers.ClientGroupListAssignmentsController.Create" />.
/// </summary>
public class ClientGroupListAssignmentWriteRequestExample
    : IExamplesProvider<ClientGroupListAssignmentWriteRequest>
{
    /// <summary>
    ///     This method assigns the subscribed "Hagezi Pro Blocklist" (the second example of
    ///     <see cref="FilterLists.FilterListDetailExample" />) to a client group. The list
    ///     identifier is the only field; to assign a different list, delete the assignment and
    ///     create another (see <see cref="Controllers.ClientGroupListAssignmentsController" />).
    /// </summary>
    public ClientGroupListAssignmentWriteRequest GetExamples() =>
        new(Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"));
}
