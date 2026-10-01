using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="AdminResetPasswordRequest" />, the
///     request body of <see cref="Controllers.UsersController.ResetPassword" />.
/// </summary>
public class AdminResetPasswordRequestExample : IExamplesProvider<AdminResetPasswordRequest>
{
    /// <summary>
    ///     This method returns the <see cref="AdminPasswordResetMode.AutoGenerate" /> mode, which
    ///     works without an SMTP server. <see cref="AdminPasswordResetMode" /> describes the other
    ///     mode, <see cref="AdminPasswordResetMode.EmailLink" />.
    /// </summary>
    public AdminResetPasswordRequest GetExamples() => new(AdminPasswordResetMode.AutoGenerate);
}
