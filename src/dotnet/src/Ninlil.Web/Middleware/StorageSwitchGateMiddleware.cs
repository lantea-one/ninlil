using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Ninlil.Web.Services.Storage;

// Define our namespace.
namespace Ninlil.Web.Middleware;

/// <summary>
///     This middleware rejects every request with a <c>503 Service Unavailable</c> while a live
///     storage-engine switch (<see cref="Controllers.StorageController.Switch" />) is in
///     progress, with two narrow exceptions: <c>GET /api/storage</c> itself (so a client polling
///     for completion is never blocked from checking), and every static/SPA asset request (so the
///     Angular admin SPA itself can keep rendering a "switch in progress" page rather than a
///     browser-level connection failure).
///     <para>
///         This exists because <see cref="StorageEngineSwitchService" />'s own switch sequence
///         briefly leaves this process pointed at a freshly migrated, still-empty target database
///         between the moment its own <c>StorageOptions</c> singleton is flipped and the moment
///         the backup captured from the previous engine finishes being restored into it. An
///         ordinary request answered from inside that window would see a database missing
///         everything the operator expects -- for example, <c>ZonesController.List</c> returning
///         an empty zone list moments before every zone reappears once the restore finishes --
///         which is a confusing, silently-incomplete answer rather than a clear "try again in a
///         moment" one. Registered early in the pipeline, immediately after exception handling
///         and reverse-proxy trust gating, so it catches every REST action this switch's own
///         narrow window could otherwise affect.
///     </para>
/// </summary>
/// <param name="next">The next delegate in the pipeline.</param>
/// <param name="state">The shared in-progress flag this middleware reads on every request.</param>
public sealed class StorageSwitchGateMiddleware(
    RequestDelegate next,
    StorageEngineSwitchState state
)
{
    /// <summary>
    ///     This method is the actual middleware entry point, invoked once per request.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!state.IsSwitchInProgress || IsExempt(context))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(
            """{"detail":"A storage-engine switch is currently in progress. Try again shortly."}"""
        );
    }

    /// <summary>
    ///     This method decides whether a request is exempt from this gate -- every
    ///     non-<c>/api</c> request (the SPA's own static assets, so it can keep rendering while a
    ///     switch runs) and <c>GET /api/storage</c> itself (so a client can keep polling for
    ///     completion).
    /// </summary>
    private static bool IsExempt(HttpContext context)
    {
        PathString path = context.Request.Path;

        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            return true;

        return path.StartsWithSegments("/api/storage", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsGet(context.Request.Method);
    }
}
