// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the client presence settings (<c>ClientPresenceOptions</c>)
///     that <c>GET /api/client-presence-settings</c> returns. Nothing in it is secret, so a
///     <c>PATCH</c> to the same endpoint accepts the same fields.
/// </summary>
/// <param name="OnlineWindow">How recently a client must have been seen to be reported online. Defaults to 10 minutes.</param>
/// <param name="FlushInterval">How often buffered sightings are written to storage. Defaults to 30 seconds.</param>
/// <param name="EnableNeighborTableSignal">Whether the kernel neighbor table is sampled as a presence signal. Defaults to <see langword="true" />.</param>
/// <param name="NeighborTablePollInterval">How often the kernel neighbor table is sampled. Defaults to 15 seconds.</param>
public record ClientPresenceSettingsDetail(
    TimeSpan OnlineWindow,
    TimeSpan FlushInterval,
    bool EnableNeighborTableSignal,
    TimeSpan NeighborTablePollInterval
);
