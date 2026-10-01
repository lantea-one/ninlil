using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for one <see cref="UserSummary" />, as returned by
///     <see cref="Controllers.UsersController.Get" /> and
///     <see cref="Controllers.UsersController.Create" />.
/// </summary>
public class UserSummaryExample : IExamplesProvider<UserSummary>
{
    /// <summary>
    ///     This method returns an enabled operator with two-factor authentication, one role and one
    ///     direct permission the role does not grant.
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
