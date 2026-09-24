using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="UpdateUserRequest" />, referenced by
///     <see cref="Controllers.UsersController.Update" />.
/// </summary>
public class UpdateUserRequestExample : IExamplesProvider<UpdateUserRequest>
{
    /// <summary>
    ///     This method returns a profile update that leaves the account enabled.
    /// </summary>
    public UpdateUserRequest GetExamples() => new("Jamie Smith", "jamie.smith@example.com", false);
}
