// Define our namespace.
namespace Ninlil.Dns.Common.Exceptions;

/// <summary>
///     This is the root of the small, HTTP-semantic exception hierarchy every administrative
///     (REST-API-triggered) operation across this project throws to report a genuinely
///     understood, named domain failure -- one worth returning to a caller as its own clean,
///     specific error response, rather than as a generic, unhandled .NET exception. Deliberately
///     framework-free: this type, and every type that derives from it, carries no dependency on
///     ASP.NET Core or any particular transport, so it can be thrown from deep inside a
///     <c>Server.*</c> project's own business logic (zone/record validation, key management,
///     credential handling, and so on) with no knowledge of, or dependency on, how the REST API
///     eventually turns it into an HTTP response.
///     <para>
///         Three more specific subclasses -- <see cref="NotFoundException" />,
///         <see cref="ConflictException" />, and <see cref="ValidationException" /> -- cover the
///         three non-authentication/non-authorization status codes this REST surface actually
///         uses in practice. This type itself is thrown directly only for a failure that has a
///         real, specific, already-understood cause but does not fit any of those three -- a
///         cryptographic operation failing against its own underlying provider, a storage write
///         failing for a reason worth naming rather than treating as an opaque bug, and so on. A
///         caller sees this mapped to a generic <c>500</c> status, but the response body still
///         names the real, understood failure rather than the framework's own bare,
///         stack-trace-free "an unexpected error occurred" fallback that a genuinely unhandled
///         exception receives instead.
///     </para>
///     <para>
///         This hierarchy is deliberately never thrown from anywhere along
///         <c>DnsQueryProcessingPipeline</c>'s own query-answering hot path -- a query for a name
///         that does not exist is a normal <c>NXDOMAIN</c> response, not an error condition, and
///         that path never blocks on anything unbounded, which exception-based control flow would
///         risk. This hierarchy exists purely for administrative operations triggered through the
///         REST API.
///     </para>
/// </summary>
public class NinlilException : Exception
{
    /// <summary>
    ///     Creates a new instance of this exception with a message that is always safe to return
    ///     to an API caller verbatim -- every message passed to this constructor, anywhere in this
    ///     hierarchy, must never embed a stack trace, a connection string, a secret, or any other
    ///     internal detail a caller should not see.
    /// </summary>
    /// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
    public NinlilException(string message)
        : base(message) { }

    /// <summary>
    ///     Creates a new instance of this exception wrapping a lower-level cause -- the inner
    ///     exception is preserved for server-side logging/diagnosis, but its own message is never
    ///     itself assumed safe to return to a caller; only this outer exception's own
    ///     <paramref name="message" /> is.
    /// </summary>
    /// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
    /// <param name="innerException">The lower-level cause, preserved for server-side logging only.</param>
    public NinlilException(string message, Exception innerException)
        : base(message, innerException) { }
}
