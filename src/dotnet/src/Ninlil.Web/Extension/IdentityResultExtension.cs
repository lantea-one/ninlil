using Microsoft.AspNetCore.Identity;
using Ninlil.Dns.Common.Exceptions;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="IdentityResult" /> extension for turning a failed
///     ASP.NET Core Identity operation (a user name already taken, a password that breaks the
///     policy, and so on) into a <see cref="ValidationException" />. Identity administration
///     controllers then report it as a 400 in the same shape as any other invalid request.
/// </summary>
public static class IdentityResultExtension
{
    /// <summary>
    ///     This method throws a <see cref="ValidationException" /> with every failure of the
    ///     result, grouped by <see cref="IdentityError.Code" />, when the operation did not
    ///     succeed. It does nothing for a successful result.
    /// </summary>
    /// <param name="instance">The <see cref="IdentityResult" /> an Identity operation returned.</param>
    /// <exception cref="ValidationException">The operation did not succeed.</exception>
    public static void ThrowIfFailed(this IdentityResult instance)
    {
        if (instance.Succeeded)
            return;

        // A code rarely repeats within one result, since each failed rule has its own code, but
        // grouping keeps ToDictionary from throwing if one ever does.
        Dictionary<string, string[]> errors = instance
            .Errors.GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray()
            );

        throw new ValidationException(errors);
    }
}
