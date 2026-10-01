using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="UserProfileDetail" />, as returned by
///     <see cref="Controllers.UsersController.GetMine" /> and
///     <see cref="Controllers.UsersController.PatchMine" />.
/// </summary>
public class UserProfileDetailExample : IExamplesProvider<UserProfileDetail>
{
    /// <summary>
    ///     This method returns a caller with a display name and phone number set, and the account's
    ///     e-mail address, which cannot be changed here.
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
