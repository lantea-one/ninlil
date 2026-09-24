using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a single
///     <see cref="UserSummary" />, referenced by <see cref="Controllers.UsersController.Get" />
///     and <see cref="Controllers.UsersController.Create" />.
/// </summary>
public class UserSummaryExample : IExamplesProvider<UserSummary>
{
    /// <summary>
    ///     This method returns an enabled, two-factor-enrolled operator holding one role plus one
    ///     directly-granted permission that role does not itself include.
    /// </summary>
    public UserSummary GetExamples() =>
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "jsmith",
            "jsmith@example.com",
            "Jamie Smith",
            false,
            true,
            ["dns-operator"],
            ["backup:read"]
        );
}
