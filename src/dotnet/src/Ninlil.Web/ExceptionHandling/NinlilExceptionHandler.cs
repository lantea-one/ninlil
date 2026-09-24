using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using ValidationException = Ninlil.Dns.Common.Exceptions.ValidationException;

// Define our namespace.
namespace Ninlil.Web.ExceptionHandling;

/// <summary>
///     This is the one place every exception this REST API's own controllers let escape gets
///     turned into an HTTP response -- registered as an <see cref="IExceptionHandler" /> via
///     <c>AddExceptionHandler&lt;NinlilExceptionHandler&gt;()</c> and invoked by
///     <c>UseExceptionHandler()</c> ahead of every other middleware in this project's pipeline.
///     Every response this handler produces is RFC 7807 <see cref="ProblemDetails" />
///     (<c>application/problem+json</c>), so a caller of any converted endpoint sees exactly one
///     consistent error shape, whether the failure is a named domain condition
///     (<see cref="NinlilException" /> and its three subclasses) or a genuinely unhandled bug.
///     <para>
///         An action that still returns an ordinary <see cref="Microsoft.AspNetCore.Mvc.ActionResult" />
///         for its own error paths -- <c>NotFound()</c>, <c>Conflict("...")</c>,
///         <c>BadRequest("...")</c>, and so on, everywhere this project has not yet converted to
///         throwing one of the four exception types below -- never reaches this handler at all:
///         an already-produced action result is written directly by MVC's own result-execution
///         pipeline, with no exception ever thrown, so this handler is purely additive and cannot
///         interfere with or double-handle a controller's own existing error handling.
///     </para>
/// </summary>
/// <param name="problemDetailsService">
///     Writes the finished <see cref="ProblemDetails" /> body onto the response, honoring
///     whatever content-negotiation/formatting ASP.NET Core's own <c>AddProblemDetails()</c>
///     registration configured.
/// </param>
/// <param name="logger">
///     Records every exception this handler processes server-side -- with full detail (including
///     a stack trace) for a genuinely unhandled exception, since that detail is never returned to
///     the caller and exists purely so an operator can diagnose the real bug from this server's
///     own logs.
/// </param>
public sealed class NinlilExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<NinlilExceptionHandler> logger
) : IExceptionHandler
{
    /// <summary>
    ///     This method builds the <see cref="ProblemDetails" /> body for whichever exception
    ///     reached this handler, sets the matching HTTP status code on the response, and hands the
    ///     finished body to <see cref="IProblemDetailsService" /> to actually write. Always
    ///     returns <see langword="true" /> -- every exception, named or not, is handled by the
    ///     <c>default</c> branch below at the very least, so this handler never lets an exception
    ///     fall through to a second handler (there is none registered) or to the framework's own
    ///     bare, unhandled-exception response.
    /// </summary>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        ProblemDetails problemDetails = BuildProblemDetails(httpContext, exception);

        httpContext.Response.StatusCode =
            problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = problemDetails,
            }
        );
    }

    /// <summary>
    ///     This method classifies an exception against this project's own four-type hierarchy and
    ///     builds the matching <see cref="ProblemDetails" /> body -- pattern-matching on the
    ///     exception's own concrete type, from most specific to least specific, so a
    ///     <see cref="ValidationException" />/<see cref="NotFoundException" />/
    ///     <see cref="ConflictException" /> is always classified correctly even though every one
    ///     of them also happens to satisfy the final, catch-all <see cref="NinlilException" />
    ///     case.
    /// </summary>
    private ProblemDetails BuildProblemDetails(HttpContext httpContext, Exception exception)
    {
        // A validation failure carrying a field-keyed error breakdown (an IdentityResult with
        // more than one distinct failure, for example) gets the exact same response shape ASP.NET
        // Core's own automatic model-state validation already produces -- an "errors" dictionary
        // alongside the usual title/detail/status -- so a caller never has to distinguish "a
        // field-level validation failure the framework caught" from "a field-level validation
        // failure this project's own business logic caught" by response shape alone.
        if (exception is ValidationException { Errors.Count: > 0 } fieldValidationException)
        {
            return new HttpValidationProblemDetails(fieldValidationException.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Detail = fieldValidationException.Message,
                Type = ProblemTypeUri(StatusCodes.Status400BadRequest),
            };
        }

        if (exception is ValidationException singleMessageValidationException)
        {
            return NewProblemDetails(
                StatusCodes.Status400BadRequest,
                "Invalid request.",
                singleMessageValidationException.Message
            );
        }

        if (exception is NotFoundException notFoundException)
        {
            ProblemDetails notFoundProblem = NewProblemDetails(
                StatusCodes.Status404NotFound,
                "Not found.",
                notFoundException.Message
            );
            notFoundProblem.Extensions["entityType"] = notFoundException.EntityType;
            notFoundProblem.Extensions["entityId"] = notFoundException.EntityId;

            return notFoundProblem;
        }

        if (exception is ConflictException conflictException)
        {
            return NewProblemDetails(
                StatusCodes.Status409Conflict,
                "Conflict.",
                conflictException.Message
            );
        }

        // A NinlilException reaching this point is the catch-all case: a named, understood
        // domain failure that does not fit any of the three more specific types above. Its own
        // message is, by this hierarchy's own documented contract, already safe to return to the
        // caller verbatim -- unlike a genuinely unhandled exception below, no correlation id or
        // extra server-side-only logging is needed, though a warning is still logged so an
        // operator can see how often a named failure of this shape is actually occurring.
        if (exception is NinlilException ninlilOneException)
        {
            logger.LogWarning(
                ninlilOneException,
                "A named domain failure occurred while processing {Method} {Path}.",
                httpContext.Request.Method,
                httpContext.Request.Path
            );

            return NewProblemDetails(
                StatusCodes.Status500InternalServerError,
                "A server error occurred.",
                ninlilOneException.Message
            );
        }

        // A genuinely unhandled exception -- a real bug, not a named domain failure this project
        // anticipated. The full exception (message, stack trace, every inner exception) is logged
        // server-side with a correlation id; the caller receives only that same correlation id and
        // a generic message, never the exception's own message or any part of its stack trace, so
        // an internal detail (a connection string, a file path, a raw SQL fragment) can never leak
        // through an error response.
        string correlationId = Guid.NewGuid().ToString("n");
        logger.LogError(
            exception,
            "An unhandled exception occurred while processing {Method} {Path}. CorrelationId={CorrelationId}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            correlationId
        );

        ProblemDetails unhandledProblem = NewProblemDetails(
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            "An unexpected error occurred while processing this request. If this keeps happening, "
                + "contact your administrator with the correlation id below."
        );
        unhandledProblem.Extensions["correlationId"] = correlationId;

        return unhandledProblem;
    }

    /// <summary>
    ///     This method builds a plain <see cref="ProblemDetails" /> instance for the given status
    ///     code, title, and detail message -- the shared shape every branch above that does not
    ///     need a field-keyed error breakdown builds its own response from.
    /// </summary>
    private static ProblemDetails NewProblemDetails(int statusCode, string title, string detail) =>
        new()
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = ProblemTypeUri(statusCode),
        };

    /// <summary>
    ///     This method builds the conventional <c>https://httpstatuses.io/{code}</c> reference
    ///     URI RFC 7807's own <c>type</c> member expects -- a stable, human-readable page
    ///     describing what the status code itself means, not specific to this project.
    /// </summary>
    private static string ProblemTypeUri(int statusCode) => $"https://httpstatuses.io/{statusCode}";
}
