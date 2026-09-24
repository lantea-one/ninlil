using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
///         Since a browser cannot attach an <c>Authorization</c> header to a WebSocket
///         handshake, the JWT bearer scheme is configured (see <c>Program.cs</c>) to also accept
///         the token from this hub's own connection query string, the standard ASP.NET Core
///         SignalR + JWT integration pattern.
///     </para>
/// </summary>
[Authorize]
public sealed class DnsEventsHub : Hub;
