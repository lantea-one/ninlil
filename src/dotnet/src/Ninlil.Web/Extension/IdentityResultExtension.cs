using Microsoft.AspNetCore.Identity;
using Ninlil.Dns.Common.Exceptions;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="IdentityResult" /> extension for turning a failed
///     ASP.NET Core Identity operation (creating a user with an already-taken name, a password
///     that does not meet the configured policy, and so on) into this project's own
///     <see cref="ValidationException" />, so every identity-administration controller reports a
///     failed <see cref="IdentityResult" /> the exact same way it reports every other malformed or
///     invalid request, rather than re-deriving its own error-response shape independently.
/// </summary>
public static class IdentityResultExtension
{
    /// <summary>
    ///     This method throws a <see cref="ValidationException" /> carrying every one of this
    ///     result's own failures, keyed by their own <see cref="IdentityError.Code" />, when the
    ///     operation did not succeed. Does nothing at all for a successful result.
    /// </summary>
    /// <param name="instance">The <see cref="IdentityResult" /> an Identity operation returned.</param>
    /// <exception cref="ValidationException">The operation did not succeed.</exception>
    public static void ThrowIfFailed(this IdentityResult instance)
    {
        if (instance.Succeeded)
            return;

        // IdentityError.Code rarely repeats within a single result (each failed rule -- a
        // duplicate name, a weak password, and so on -- has its own distinct code), but grouping
        // rather than assuming uniqueness keeps this safe even if it ever did.
        Dictionary<string, string[]> errors = instance
            .Errors.GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray()
            );

        throw new ValidationException(errors);
    }
}
