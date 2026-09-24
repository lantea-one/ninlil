using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="AdminResetPasswordResult" />, referenced by
///     <see cref="Controllers.UsersController.ResetPassword" />.
/// </summary>
public class AdminResetPasswordResultExample : IExamplesProvider<AdminResetPasswordResult>
{
    /// <summary>
    ///     This method returns the auto-generate-and-reveal-once delivery mode's own result --
    ///     the newly-set password, shown exactly this once. An <c>EmailLink</c> mode result
    ///     instead carries a <see langword="null" /><c>generatedPassword</c>, since that mode
    ///     never learns the account holder's new password at all.
    /// </summary>
    public AdminResetPasswordResult GetExamples() =>
        new(AdminPasswordResetMode.AutoGenerate, "xK9#mQ2wLp7$vN4rT8bE");
}
