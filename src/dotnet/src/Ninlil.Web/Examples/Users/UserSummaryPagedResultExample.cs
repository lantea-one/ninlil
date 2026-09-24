using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="UserSummary" />, referenced by <see cref="Controllers.UsersController.List" />.
/// </summary>
public class UserSummaryPagedResultExample : IExamplesProvider<PagedResult<UserSummary>>
{
    /// <summary>
    ///     This method returns one page of two representative accounts: the bootstrap
    ///     administrator (holding the wildcard permission this project's own cold-start solution
    ///     grants it) and an ordinary, single-role operator, in the exact envelope a real list
    ///     response carries.
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
