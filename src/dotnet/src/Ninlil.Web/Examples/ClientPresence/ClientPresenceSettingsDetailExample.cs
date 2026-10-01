using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientPresence;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientPresenceSettingsDetail" />, as
///     returned by <c>GET /api/client-presence-settings</c>: the defaults (a 10-minute online
///     window, 30-second flushes, neighbor table sampling on every 15 seconds).
/// </summary>
public class ClientPresenceSettingsDetailExample : IExamplesProvider<ClientPresenceSettingsDetail>
{
    /// <inheritdoc />
    public ClientPresenceSettingsDetail GetExamples() =>
        new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30), true, TimeSpan.FromSeconds(15));
}
