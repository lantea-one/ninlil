using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientListAssignments;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientListAssignmentWriteRequest" />,
///     the request body of <see cref="Controllers.ClientListAssignmentsController.Create" />.
/// </summary>
public class ClientListAssignmentWriteRequestExample
    : IExamplesProvider<ClientListAssignmentWriteRequest>
{
    /// <summary>
    ///     This method assigns the subscribed "Hagezi Pro Blocklist" (the second example of
    ///     <see cref="FilterLists.FilterListDetailExample" />) directly to a known client. The list
    ///     identifier is the only field; to assign a different list, delete the assignment and
    ///     create another (see <see cref="Controllers.ClientListAssignmentsController" />).
    /// </summary>
    public ClientListAssignmentWriteRequest GetExamples() =>
        new(Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"));
}
