using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Ninlil.Web.Services.Storage;

// Define our namespace.
namespace Ninlil.Web.Middleware;

/// <summary>
///     This middleware answers every <c>/api</c> request with <c>503 Service Unavailable</c> while
///     a live storage-engine switch (<see cref="Controllers.StorageController.Switch" />) is in
///     progress, except <c>GET /api/storage</c>, so a client can keep polling for completion.
///     Requests outside <c>/api</c> pass through, so the admin console's static files still load
///     and it can show a "switch in progress" page instead of a connection error.
///     <para>
///         The switch sequence in <see cref="StorageEngineSwitchService" /> briefly leaves the
///         process using the freshly migrated, still-empty target database, between switching
///         the <c>StorageOptions</c> singleton and finishing the restore of the backup taken from
///         the old engine. A request answered in that window would see missing data (for example
///         <c>ZonesController.List</c> returning no zones moments before they all reappear),
///         which is more confusing than "try again in a moment". <c>Program.cs</c> registers it
///         right after exception handling and reverse-proxy trust gating, ahead of the endpoints.
///     </para>
/// </summary>
/// <param name="next">The next delegate in the pipeline.</param>
/// <param name="state">The shared in-progress flag read on every request.</param>
public sealed class StorageSwitchGateMiddleware(
    RequestDelegate next,
    StorageEngineSwitchState state
)
{
    /// <summary>
    ///     This method is the middleware entry point, called once per request.
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
    ///     This method decides whether a request is exempt from the gate: every request outside
    ///     <c>/api</c>, and <c>GET</c> requests under <c>/api/storage</c>.
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
