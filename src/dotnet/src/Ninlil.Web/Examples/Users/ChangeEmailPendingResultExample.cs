using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="ChangeEmailPendingResult" />, the 202
///     answer of <see cref="Controllers.UsersController.ChangeMyEmail" />.
/// </summary>
public class ChangeEmailPendingResultExample : IExamplesProvider<ChangeEmailPendingResult>
{
    /// <summary>
    ///     This method returns the answer to a change that waits for the link in the new
    ///     address's mailbox to be opened.
    /// </summary>
    public ChangeEmailPendingResult GetExamples() => new(true);
}
