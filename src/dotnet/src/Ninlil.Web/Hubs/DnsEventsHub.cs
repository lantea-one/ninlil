using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Ninlil.Dns.Identity.Authorization;

// Define our namespace.
namespace Ninlil.Web.Hubs;

/// <summary>
///     This hub pushes a curated subset of Server.Core's own internal event bus
///     (<c>DnsQueryReceivedEvent</c>/<c>DnsQueryAnsweredEvent</c>/<c>DnsZoneChangedEvent</c>/
///     <c>DnsCacheEvictedEvent</c>) to authenticated real-time clients, so the Angular admin
///     dashboard can show live query activity without polling a REST endpoint. Push-only --
///     clients only ever receive broadcasts (see
///     <see cref="DnsEventsHubBroadcaster" />); this class declares no callable methods of its
///     own.
///     <para>
///         Signing in is not enough to receive an event: each event type goes only to the
///         SignalR group of the permission the equivalent REST data requires (see
///         <see cref="BroadcastPermissions" />), and a connection joins a group, in
///         <see cref="OnConnectedAsync" />, only when its user holds that permission. Without
///         this, anyone signed in would see every client address and name in the live query
///         stream, which the query log's own endpoints refuse without <c>querylog:read</c>.
///     </para>
///     <para>
///         Group membership is worked out once, when the connection opens, from the permission
///         claims in the access token it presented -- the same claims every REST request with
///         that token is authorized against. A permission granted or revoked afterwards reaches
///         the connection only when it reconnects with a newer token, so <c>Program.cs</c> maps
///         this hub with <c>CloseOnAuthenticationExpiration</c>: the server closes the connection
///         when its token expires, the client's automatic reconnect presents its current token,
///         and a stale permission never outlives the token it came from.
///     </para>
///     <para>
///         Since a browser cannot attach an <c>Authorization</c> header to a WebSocket
///         handshake, the JWT bearer scheme is configured (see <c>Program.cs</c>) to also accept
///         the token from this hub's own connection query string, the standard ASP.NET Core
///         SignalR + JWT integration pattern.
///     </para>
/// </summary>
/// <param name="authorizationService">Checks the connecting user's permissions.</param>
[Authorize]
public sealed class DnsEventsHub(IAuthorizationService authorizationService) : Hub
{
    /// <summary>
    ///     This constant contains the permission the live query stream (<c>DnsQueryReceived</c>
    ///     and <c>DnsQueryAnswered</c>) requires -- the same one <c>QueryLogController</c> and
    ///     <c>ClientActivityController</c> require to read the same queries after the fact.
    /// </summary>
    public const string QueryEventsPermission = "querylog:read";

    /// <summary>
    ///     This constant contains the permission <c>DnsZoneChanged</c> requires -- the same one
    ///     <c>ZonesController</c> requires to list or read a zone.
    /// </summary>
    public const string ZoneEventsPermission = "zones:read";

    /// <summary>
    ///     This constant contains the permission <c>DnsCacheEvicted</c> requires. No endpoint
    ///     reports cache evictions themselves, so this is the permission guarding the recursive
    ///     resolver's settings (<c>RecursiveSettingsController</c>), cache size included.
    /// </summary>
    public const string CacheEventsPermission = "recursivesettings:read";

    /// <summary>
    ///     This property contains every permission an event is broadcast under, each of which has
    ///     a group of the same name (see <see cref="GroupName" />).
    /// </summary>
    public static IReadOnlyList<string> BroadcastPermissions { get; } =
    [QueryEventsPermission, ZoneEventsPermission, CacheEventsPermission];

    /// <summary>
    ///     This method returns the SignalR group whose members hold the given permission.
    /// </summary>
    /// <param name="permission">The permission, such as <c>querylog:read</c>.</param>
    /// <returns>The group's name.</returns>
    public static string GroupName(string permission) => "permission:" + permission;

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        // Ask the authorization service rather than reading the claims ourselves, so the
        // wildcard permission and anything else PermissionAuthorizationHandler honours count
        // here exactly as they do for a REST request.
        foreach (string permission in BroadcastPermissions)
        {
            AuthorizationResult result = await authorizationService.AuthorizeAsync(
                Context.User,
                PermissionPolicyProvider.PolicyPrefix + permission
            );

            if (result.Succeeded)
                await Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    GroupName(permission),
                    Context.ConnectionAborted
                );
        }

        await base.OnConnectedAsync();
    }
}
