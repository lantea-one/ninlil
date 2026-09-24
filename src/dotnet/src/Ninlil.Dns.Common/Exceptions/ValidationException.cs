// Define our namespace.
namespace Ninlil.Dns.Common.Exceptions;

/// <summary>
///     This exception is thrown when an administrative operation's own input is malformed or
///     invalid in a way that goes beyond basic model binding -- an unparseable forwarder
///     definition string, a malformed regex/wildcard filter-list pattern, record data that does
///     not match its own declared type, an <c>IdentityResult</c> reporting one or more failures
///     (a duplicate user name, a password that does not meet the configured policy, and so on).
///     Mapped by the REST API's own shared exception handler to a <c>400</c> response.
///     <para>
///         Carries an optional, field-keyed <see cref="Errors" /> dictionary -- the same shape
///         ASP.NET Core's own automatic model-state validation already reports through
///         <c>ValidationProblemDetails.Errors</c> -- so a caller reporting more than one distinct
///         field failure at once (as ASP.NET Core Identity's own <c>IdentityResult</c> routinely
///         does) still produces the identical response shape a single-message validation failure
///         does, rather than a second, different error format for the multi-error case.
///     </para>
/// </summary>
public sealed class ValidationException : NinlilException
{
    /// <summary>
    ///     Creates a new instance of this exception carrying a single, free-form validation
    ///     failure message, with no field-keyed error breakdown.
    /// </summary>
    /// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
    public ValidationException(string message)
        : base(message)
    {
        Errors = null;
    }

    /// <summary>
    ///     Creates a new instance of this exception carrying a field-keyed breakdown of one or
    ///     more distinct validation failures -- the same shape a failed <c>IdentityResult</c>, or
    ///     a set of independently-invalid record fields, naturally produces.
    /// </summary>
    /// <param name="errors">
    ///     Every distinct validation failure, keyed by the field or error code it applies to, with
    ///     one or more human-readable messages per key.
    /// </param>
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    /// <summary>
    ///     The field-keyed breakdown of every distinct validation failure this exception carries,
    ///     or <see langword="null" /> when it was constructed from a single, free-form message
    ///     instead.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
