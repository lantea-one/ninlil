using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="IEndpointRouteBuilder" /> extension for mapping this
///     server's own <c>GET /status</c> health-check endpoint.
///     <para>
///         <b>Deliberately a Minimal API endpoint, not an MVC controller action, and deliberately
///         not the built-in <c>MapHealthChecks</c> middleware extension either</b> -- both real
///         alternatives were considered. Every other REST resource in this project is an MVC
///         controller, but this endpoint (like <c>/documentation</c>'s own <c>MapGet</c>) sits
///         outside <c>/api</c> entirely, serving operational status rather than a data resource,
///         which is exactly the same reasoning that already put Swagger/ReDoc on a bare
///         <c>MapGet</c> instead of a controller. <c>MapHealthChecks</c> itself was ruled out
///         because its own built-in response writer has no hook for this endpoint's own required
///         shape: the *same* underlying <see cref="HealthReport" /> must be rendered two
///         different ways depending on the caller's own authentication/permission state (a
///         high-level-only summary for an anonymous or unpermitted caller, the full per-component
///         breakdown *additively* for one holding <c>status:read</c>) -- calling
///         <see cref="HealthCheckService.CheckHealthAsync(System.Threading.CancellationToken)" />
///         directly and shaping the response by hand was simpler than fighting that middleware's
///         own response-writer customization point to get an authorization-state-dependent body.
///     </para>
/// </summary>
public static class EndpointRouteBuilderStatusExtension
{
    /// <summary>
    ///     This constant contains the permission a caller must hold for this endpoint's response
    ///     to include the full per-component breakdown, following this project's own
    ///     <c>&lt;resource&gt;:read</c> permission-naming convention.
    /// </summary>
    public const string ReadPermission = "status:read";

    /// <summary>
    ///     This method maps <c>GET /status</c>. No <c>[Authorize]</c>/<c>RequireAuthorization</c>
    ///     is ever applied to this endpoint -- an unauthenticated request is always answered, per
    ///     the pre-answered "serves operational status, not data" posture Swagger/ReDoc already
    ///     get -- but <see cref="HttpContext.User" /> is still populated by the pipeline's own
    ///     already-registered authentication middleware whenever a caller *does* present a valid
    ///     bearer token, which this handler reads directly to decide whether to add the
    ///     additional per-component detail.
    /// </summary>
    /// <param name="endpoints">The current <see cref="IEndpointRouteBuilder" /> instance.</param>
    /// <returns>The modified <paramref name="endpoints" /> instance.</returns>
    public static IEndpointRouteBuilder MapNinlilDnsStatus(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/status",
                async (
                    HttpContext httpContext,
                    HealthCheckService healthCheckService,
                    IAuthorizationService authorizationService,
                    CancellationToken cancellationToken
                ) =>
                {
                    // Run the real health-check sweep once regardless of who is asking -- the overall
                    // status alone (never the per-component detail) is always safe to hand back to an
                    // anonymous caller, so there is no reason to skip the check itself based on
                    // authentication state.
                    HealthReport report = await healthCheckService.CheckHealthAsync(
                        cancellationToken
                    );
                    string overallStatus = report.Status.ToString();

                    // Only an authenticated caller can even hold the status:read permission, so check
                    // IsAuthenticated first as a cheap short-circuit before bothering the
                    // authorization service at all -- an anonymous request never reaches the
                    // permission check and always gets the summary-only shape below.
                    bool includeDetail = false;

                    if (httpContext.User.Identity?.IsAuthenticated == true)
                    {
                        AuthorizationResult authorization =
                            await authorizationService.AuthorizeAsync(
                                httpContext.User,
                                PermissionPolicyProvider.PolicyPrefix + ReadPermission
                            );
                        includeDetail = authorization.Succeeded;
                    }

                    // The two response shapes this endpoint's own doc comment describes: a bare
                    // overall status with a null component list for everyone, or that same status
                    // plus the full per-component breakdown additively layered on top for a caller
                    // who actually holds the permission -- never a different overall status value
                    // between the two, only whether the detail list is populated.
                    if (!includeDetail)
                        return Results.Json(new StatusResponse(overallStatus, null));

                    List<StatusComponent> components =
                    [
                        .. report.Entries.Select(entry => new StatusComponent(
                            entry.Key,
                            entry.Value.Status.ToString(),
                            entry.Value.Description,
                            entry.Value.Exception?.Message
                        )),
                    ];

                    return Results.Json(new StatusResponse(overallStatus, components));
                }
            )
            // Results.Json hides the body type from the API explorer, so name it here; without
            // this the OpenAPI document gives /status no response schema and generated clients
            // type it as "any".
            .Produces<StatusResponse>();

        return endpoints;
    }
}
