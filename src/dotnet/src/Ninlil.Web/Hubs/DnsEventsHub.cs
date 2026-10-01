using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Ninlil.Dns.Identity.Authorization;

// Define our namespace.
namespace Ninlil.Web.Hubs;

/// <summary>
///     This hub pushes selected events from the Server.Core event bus
///     (<c>DnsQueryReceivedEvent</c>, <c>DnsQueryAnsweredEvent</c>, <c>DnsZoneChangedEvent</c> and
///     <c>DnsCacheEvictedEvent</c>) to signed-in real-time clients, so the admin console's
///     dashboard shows live query activity without polling. It is push-only:
///     <see cref="DnsEventsHubBroadcaster" /> sends the events, and this class declares no methods
///     for clients to call. <c>Program.cs</c> maps it at <c>/hubs/events</c>.
///     <para>
///         Signing in is not enough to receive an event. Each event type goes only to the
///         SignalR group of the permission the matching REST data requires (see
///         <see cref="BroadcastPermissions" />), and <see cref="OnConnectedAsync" /> adds a
///         connection to a group only when its user holds that permission. Otherwise anyone
///         signed in would see every client address and name in the live query stream, which the
///         query log endpoints refuse without <c>querylog:read</c>.
///     </para>
///     <para>
///         Group membership is decided once, when the connection opens, from the permission
///         claims in its access token, the same claims REST requests with that token are checked
///         against. A permission granted or revoked later applies only after the client
///         reconnects with a newer token, so <c>Program.cs</c> maps this hub with
///         <c>CloseOnAuthenticationExpiration</c>: the server closes the connection when its token
///         expires, the client's automatic reconnect presents its current token, and a stale
///         permission never outlives its token.
///     </para>
///     <para>
///         A browser cannot attach an <c>Authorization</c> header to a WebSocket handshake, so
///         the JWT bearer scheme also accepts the token from this hub's connection query string,
///         the usual ASP.NET Core SignalR and JWT pattern.
///     </para>
/// </summary>
/// <param name="authorizationService">Checks the connecting user's permissions.</param>
[Authorize]
public sealed class DnsEventsHub(IAuthorizationService authorizationService) : Hub
{
    /// <summary>
    ///     This constant contains the permission the live query stream (<c>DnsQueryReceived</c> and
    ///     <c>DnsQueryAnswered</c>) requires, the one <c>QueryLogController</c> and
    ///     <c>ClientActivityController</c> require to read the same queries later.
    /// </summary>
    public const string QueryEventsPermission = "querylog:read";

    /// <summary>
    ///     This constant contains the permission <c>DnsZoneChanged</c> requires, the one
    ///     <c>ZonesController</c> requires to read a zone.
    /// </summary>
    public const string ZoneEventsPermission = "zones:read";

    /// <summary>
    ///     This constant contains the permission <c>DnsCacheEvicted</c> requires. No endpoint
    ///     reports evictions, so it uses the permission guarding the recursive resolver's settings
    ///     (<c>RecursiveSettingsController</c>), cache size included, which also guards
    ///     <c>DnsCacheController</c>.
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
        // Ask the authorization service rather than reading the claims directly, so the wildcard
        // permission and anything else PermissionAuthorizationHandler honours count here as they do
        // for a REST request.
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
