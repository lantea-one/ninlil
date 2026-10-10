using Microsoft.AspNetCore.Http;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Web.Extension;
using Ninlil.Web.Routing;

// Define our namespace.
namespace Ninlil.Web.Middleware;

/// <summary>
///     This middleware connects an HTTP request to the storage router. It runs after
///     authentication and authorization and before the endpoint, which is when the controller
///     and its repositories are created, so the request scope has not resolved a database
///     context yet. It does two things:
///     <list type="bullet">
///         <item>
///             It tells the scope's <see cref="StoragePrincipal" /> which user the request works
///             for (the <c>sub</c> claim, the same for a JWT and an API token). A write in the
///             request then keeps that user on the primary for the sticky window, and a read
///             request of a user inside the window goes to the primary.
///         </item>
///         <item>
///             For a GET or HEAD request to an endpoint marked <see cref="ReadFromReplicaAttribute" />
///             it sets the scope's <see cref="StorageReadIntent" /> to the replica. The router
///             still decides: with no replica, an ineligible one or a sticky user the scope reads
///             the primary.
///         </item>
///     </list>
///     When the request ends the middleware starts the sticky window again if the request wrote
///     (<see cref="StoragePrincipal.Restamp" />), which covers a commit that came late.
///     <para>
///         Nothing before this middleware may resolve a repository in the request scope, or the
///         intent is already fixed and setting it throws. The API token scheme therefore works in
///         a scope of its own (<c>ApiTokenAuthenticationHandler</c>), and the JWT scheme reads no
///         database. Sign-in endpoints, DHCP packet handling and backup export never carry the
///         attribute, so they always read the primary.
///     </para>
/// </summary>
/// <param name="next">The next delegate in the pipeline.</param>
public sealed class StorageReadIntentMiddleware(RequestDelegate next)
{
    /// <summary>
    ///     This method runs the middleware for one request.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <param name="intent">The read intent of the request scope.</param>
    /// <param name="principal">The storage principal of the request scope.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        StorageReadIntent intent,
        StoragePrincipal principal
    )
    {
        Guid? userId = context.User?.GetUserId();
        if (userId.HasValue)
            principal.Set(StoragePrincipal.KeyForUser(userId.Value));

        bool isRead =
            HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);

        if (
            isRead
            && context.GetEndpoint()?.Metadata.GetMetadata<ReadFromReplicaAttribute>() is not null
        )
            intent.Set(StorageReadTargetEnum.Replica);

        try
        {
            await next(context);
        }
        finally
        {
            principal.Restamp();
        }
    }
}
