using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="AdminResetPasswordResult" />, as
///     returned by <see cref="Controllers.UsersController.ResetPassword" />.
/// </summary>
public class AdminResetPasswordResultExample : IExamplesProvider<AdminResetPasswordResult>
{
    /// <summary>
    ///     This method returns the result of the <c>AutoGenerate</c> mode: the new password, shown
    ///     only here. In <c>EmailLink</c> mode <c>generatedPassword</c> is <see langword="null" />,
    ///     since the server never learns the new password.
    /// </summary>
    public AdminResetPasswordResult GetExamples() =>
        new(AdminPasswordResetMode.AutoGenerate, "xK9#mQ2wLp7$vN4rT8bE");
}
