using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using ValidationException = Ninlil.Dns.Common.Exceptions.ValidationException;

// Define our namespace.
namespace Ninlil.Web.ExceptionHandling;

/// <summary>
///     This class turns every exception that escapes a controller or middleware into an HTTP
///     response. It is registered with <c>AddExceptionHandler&lt;NinlilExceptionHandler&gt;()</c>
///     and run by <c>UseExceptionHandler()</c>, the first middleware in <c>Program.cs</c>. Every
///     response is an RFC 7807 <see cref="ProblemDetails" /> (<c>application/problem+json</c>),
///     so callers see one error shape whether the failure is a named domain condition
///     (<see cref="NinlilException" /> and its three subclasses) or an unhandled bug.
///     <para>
///         An action that returns an error result itself (<c>NotFound()</c>,
///         <c>Conflict("...")</c>, <c>BadRequest("...")</c>) never reaches this handler, because
///         no exception is thrown; MVC writes that result directly.
///     </para>
/// </summary>
/// <param name="problemDetailsService">
///     Writes the <see cref="ProblemDetails" /> body, using the formatting that
///     <c>AddProblemDetails()</c> configured.
/// </param>
/// <param name="logger">
///     Logs each handled exception. An unhandled exception is logged in full, with its stack
///     trace, since the caller never sees that detail.
/// </param>
public sealed class NinlilExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<NinlilExceptionHandler> logger
) : IExceptionHandler
{
    /// <summary>
    ///     This method builds the <see cref="ProblemDetails" /> for the exception, sets the
    ///     matching status code and writes the body. Every exception is handled, at worst by the
    ///     final catch-all branch of <see cref="BuildProblemDetails" />, so none falls through to
    ///     the framework's bare error response.
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
    ///     This method maps an exception to a <see cref="ProblemDetails" />: 400 for
    ///     <see cref="ValidationException" />, 404 for <see cref="NotFoundException" />, 409 for
    ///     <see cref="ConflictException" />, and 500 for any other exception. The specific types
    ///     are checked before <see cref="NinlilException" />, which they all derive from.
    /// </summary>
    private ProblemDetails BuildProblemDetails(HttpContext httpContext, Exception exception)
    {
        // A validation failure with per-field errors (an IdentityResult with several failures, for
        // example) gets the same shape as ASP.NET Core's automatic model validation: an "errors"
        // dictionary alongside title, detail and status. Callers can then handle both the same way.
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

        // Any other NinlilException is a named, expected failure whose message is safe to return as
        // is, so no correlation id is needed. A warning is still logged so an operator can see how
        // often it happens.
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

        // An unhandled exception is a bug. It is logged in full with a correlation id, and the
        // caller gets only that id and a generic message, never the exception message or stack
        // trace, so no internal detail (a connection string, a file path, a SQL fragment) leaks
        // into a response.
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
    ///     This method builds a <see cref="ProblemDetails" /> with the given status, title and
    ///     detail, for every branch that has no per-field errors.
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
    ///     This method builds the <c>https://httpstatuses.io/{code}</c> page URI used as the RFC
    ///     7807 <c>type</c> member, which describes the status code in general.
    /// </summary>
    private static string ProblemTypeUri(int statusCode) => $"https://httpstatuses.io/{statusCode}";
}
