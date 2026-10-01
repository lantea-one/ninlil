using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="UserSummary" />, as
///     returned by <see cref="Controllers.UsersController.List" />.
/// </summary>
public class UserSummaryPagedResultExample : IExamplesProvider<PagedResult<UserSummary>>
{
    /// <summary>
    ///     This method returns the bootstrap administrator, who gets every permission through the
    ///     <c>Administrators</c> role rather than directly, and an ordinary operator. The real
    ///     bootstrap account defaults to the user name <c>admin</c> and display name
    ///     <c>Administrator</c>; the example uses other values.
    /// </summary>
    public PagedResult<UserSummary> GetExamples() =>
        new(
            [
                new UserSummary(
                    Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    "administrator",
                    "administrator@localhost.local",
                    "Bootstrap Administrator",
                    false,
                    false,
                    ["Administrators"],
                    []
                ),
                new UserSummary(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "jsmith",
                    "jsmith@example.com",
                    "Jamie Smith",
                    false,
                    true,
                    ["dns-operator"],
                    ["backup:read"]
                ),
            ],
            0,
            50,
            2
        );
}
