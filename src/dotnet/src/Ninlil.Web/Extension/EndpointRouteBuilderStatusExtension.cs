using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="IEndpointRouteBuilder" /> extension for mapping the
///     <c>GET /status</c> health endpoint, whose response is <see cref="StatusResponse" />.
///     <para>
///         <b>Why a Minimal API endpoint.</b> It sits outside <c>/api</c> and serves operational
///         status rather than a resource, like the API documentation endpoints, so it is not an
///         MVC controller. The built-in <c>MapHealthChecks</c> does not fit either: one
///         <see cref="HealthReport" /> has to be rendered two ways depending on the caller (the
///         overall status for anyone, plus the per-component list for a caller holding
///         <c>status:read</c>), and calling
///         <see cref="HealthCheckService.CheckHealthAsync(System.Threading.CancellationToken)" />
///         directly is simpler than bending that middleware's response writer to the caller's
///         permissions.
///     </para>
/// </summary>
public static class EndpointRouteBuilderStatusExtension
{
    /// <summary>
    ///     This constant contains the permission a caller needs for the response to include the
    ///     per-component list.
    /// </summary>
    public const string ReadPermission = "status:read";

    /// <summary>
    ///     This method maps <c>GET /status</c>. The endpoint has no authorization requirement, so
    ///     an anonymous request is answered, as for the API documentation. When the caller sends a
    ///     valid token, the authentication middleware still fills in
    ///     <see cref="HttpContext.User" />, and the handler checks it to decide whether to add the
    ///     per-component list.
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
                    // Run the health checks for every caller: the overall status alone is safe to
                    // give an anonymous caller.
                    HealthReport report = await healthCheckService.CheckHealthAsync(
                        cancellationToken
                    );
                    string overallStatus = report.Status.ToString();

                    // Only an authenticated caller can hold status:read, so an anonymous request
                    // skips the authorization service and gets the summary.
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

                    // Both shapes carry the same overall status; only the component list differs.
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
