using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="UserProfileDetail" />, referenced by
///     <see cref="Controllers.UsersController.GetMine" /> and
///     <see cref="Controllers.UsersController.PatchMine" />.
/// </summary>
public class UserProfileDetailExample : IExamplesProvider<UserProfileDetail>
{
    /// <summary>
    ///     This method returns a caller who has already set both a display name and a phone
    ///     number, alongside their account's own e-mail address (read-only through this pair of
    ///     actions).
    /// </summary>
    public UserProfileDetail GetExamples() =>
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "jsmith",
            "jsmith@example.com",
            "Jamie Smith",
            "+1-555-0100"
        );
}
