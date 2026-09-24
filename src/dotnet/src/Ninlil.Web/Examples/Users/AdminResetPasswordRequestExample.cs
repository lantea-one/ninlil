using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="AdminResetPasswordRequest" />, referenced by
///     <see cref="Controllers.UsersController.ResetPassword" />.
/// </summary>
public class AdminResetPasswordRequestExample : IExamplesProvider<AdminResetPasswordRequest>
{
    /// <summary>
    ///     This method returns the auto-generate-and-reveal-once delivery mode -- the mode that
    ///     works with no SMTP server configured, this project's homelab-first default. See
    ///     <see cref="AdminPasswordResetMode" />'s own doc comment for the other mode,
    ///     <see cref="AdminPasswordResetMode.EmailLink" />.
    /// </summary>
    public AdminResetPasswordRequest GetExamples() => new(AdminPasswordResetMode.AutoGenerate);
}
