using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Ninlil.Web.Extension;

// Define our namespace.
namespace Ninlil.Web.ExceptionHandling;

/// <summary>
///     This class is the safety net for every <c>PATCH</c> action that takes a
///     <c>[FromBody] JsonElement</c> merge patch. It is registered globally in
///     <c>Program.cs</c> (<c>AddControllers</c>) and acts only on actions that have such a
///     parameter. It does two things.
///     <list type="bullet">
///         <item>
///             Before the action runs, a body that is not a JSON object (<c>[]</c>, <c>"x"</c>,
///             <c>5</c>, <c>null</c>) is answered with 400. Without this the action would find no
///             field and report "Nothing to change" with 200 (RFC 7396 §2 defines a merge patch
///             as an object).
///         </item>
///         <item>
///             When the action throws <see cref="InvalidOperationException" /> or
///             <see cref="FormatException" /> from <c>System.Text.Json</c>, the exception is
///             turned into 400. That is what <see cref="JsonElement.GetInt32" /> and its siblings
///             throw for a field of the wrong JSON type. The source check and the restriction to
///             these actions keep the rule narrow: inside them the only <c>System.Text.Json</c>
///             reads are of the request body, so a server-side JSON bug elsewhere stays a 500.
///             Every other exception passes through to <see cref="NinlilExceptionHandler" />.
///         </item>
///     </list>
///     The filter names no field. Actions that read with the typed readers of
///     <see cref="JsonElementMergePatchExtension" /> answer with a field-naming 400 of their
///     own and never reach the second rule; the filter covers the actions that still use raw
///     reads and any field a coder misses. The 400 body has the same RFC 7807 shape and
///     <c>type</c> URI scheme that <see cref="NinlilExceptionHandler" /> writes.
/// </summary>
public sealed class MergePatchBodyFilter : IActionFilter
{
    /// <summary>
    ///     This constant is the start of the <see cref="Exception.Source" /> of an exception
    ///     thrown by <c>System.Text.Json</c>. The runtime reports
    ///     <c>System.Text.Json.Rethrowable</c> for the wrong-kind and format errors of
    ///     <see cref="JsonElement" />, so the check is a prefix match, not equality.
    /// </summary>
    private const string JsonSourcePrefix = "System.Text.Json";

    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!HasMergePatchParameter(context.ActionDescriptor))
            return;

        foreach (object argument in context.ActionArguments.Values)
        {
            if (argument is JsonElement { ValueKind: not JsonValueKind.Object })
            {
                context.Result = BadRequest(
                    "A PATCH body must be a JSON object (RFC 7396 merge patch)."
                );
                return;
            }
        }
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (
            context.Exception is InvalidOperationException or FormatException
            && context.Exception.Source is { } source
            && source.StartsWith(JsonSourcePrefix, StringComparison.Ordinal)
            && HasMergePatchParameter(context.ActionDescriptor)
        )
        {
            context.Result = BadRequest("A field in the request body has the wrong JSON type.");
            context.ExceptionHandled = true;
        }
    }

    /// <summary>
    ///     This method tells whether the action takes a <see cref="JsonElement" /> parameter,
    ///     which in this API is always a merge-patch body.
    /// </summary>
    private static bool HasMergePatchParameter(ActionDescriptor descriptor) =>
        descriptor is ControllerActionDescriptor action
        && action
            .MethodInfo.GetParameters()
            .Any(parameter => parameter.ParameterType == typeof(JsonElement));

    /// <summary>
    ///     This method builds the 400 <see cref="ProblemDetails" /> result in the shape
    ///     <see cref="NinlilExceptionHandler" /> uses for a validation failure.
    /// </summary>
    private static ObjectResult BadRequest(string detail) =>
        new(
            new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request.",
                Detail = detail,
                Type = "https://httpstatuses.io/400",
            }
        )
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };
}
