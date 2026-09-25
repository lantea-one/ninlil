// Define our namespace.
namespace Ninlil.Dns.Common.Exceptions;

/// <summary>
///     This exception is thrown when the input to an administrative operation is invalid in a way
///     model binding does not catch: an unparseable forwarder definition, a malformed regex or
///     wildcard filter pattern, record data that does not match its type, or a failed
///     <c>IdentityResult</c> (duplicate user name, weak password, and so on). The REST API's
///     shared exception handler maps it to a <c>400</c> response.
///     <para>
///         The optional <see cref="Errors" /> dictionary has the same field-keyed shape as
///         <c>ValidationProblemDetails.Errors</c> from ASP.NET Core model validation, so several
///         field failures at once (as an <c>IdentityResult</c> often reports) produce the same
///         response format as a single message.
///     </para>
/// </summary>
public sealed class ValidationException : NinlilException
{
    /// <summary>
    ///     Creates a new instance of this exception with a single free-form message and no
    ///     field-keyed errors.
    /// </summary>
    /// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
    public ValidationException(string message)
        : base(message)
    {
        Errors = null;
    }

    /// <summary>
    ///     Creates a new instance of this exception with field-keyed errors, such as those from a
    ///     failed <c>IdentityResult</c> or several invalid record fields.
    /// </summary>
    /// <param name="errors">
    ///     The validation failures, keyed by field or error code, with one or more readable messages
    ///     per key.
    /// </param>
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    /// <summary>
    ///     The field-keyed validation failures, or <see langword="null" /> when the exception was
    ///     created from a single message.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
