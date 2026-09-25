// Define our namespace.
namespace Ninlil.Dns.Common.Exceptions;

/// <summary>
///     This is the root of the exception hierarchy that administrative (REST API) operations throw
///     to report a known, named failure. Such a failure becomes a specific error response instead
///     of a generic unhandled exception. The hierarchy has no dependency on ASP.NET Core, so
///     <c>Server.*</c> projects can throw it from business logic (zone and record validation, key
///     management, credential handling) without knowing how the REST API turns it into HTTP.
///     <para>
///         <see cref="NotFoundException" />, <see cref="ConflictException" /> and
///         <see cref="ValidationException" /> cover the non-auth status codes the API uses. This
///         base type is thrown directly for a known failure that fits none of them, such as a
///         cryptographic provider failing or a storage write failing for a nameable reason. It maps
///         to <c>500</c>, but the body names the failure instead of the generic "an unexpected
///         error occurred" that an unhandled exception gets.
///     </para>
///     <para>
///         The query path (<c>DnsQueryProcessingPipeline</c>) never throws these. A missing name
///         there is a normal <c>NXDOMAIN</c> response, not an error, and the hot path avoids
///         exception-based control flow.
///     </para>
/// </summary>
public class NinlilException : Exception
{
    /// <summary>
    ///     Creates a new instance of this exception with a message that is returned to the API caller
    ///     as-is. A message anywhere in this hierarchy must never contain a stack trace, connection
    ///     string, secret or other internal detail.
    /// </summary>
    /// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
    public NinlilException(string message)
        : base(message) { }

    /// <summary>
    ///     Creates a new instance of this exception that wraps a lower-level cause. The inner
    ///     exception is kept for server-side logging; its message is not assumed safe to return, only
    ///     <paramref name="message" /> is.
    /// </summary>
    /// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
    /// <param name="innerException">The lower-level cause, preserved for server-side logging only.</param>
    public NinlilException(string message, Exception innerException)
        : base(message, innerException) { }
}
