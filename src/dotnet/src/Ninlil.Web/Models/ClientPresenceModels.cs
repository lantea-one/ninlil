// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>ClientPresenceOptions</c>. No secret material is involved, so this record's own fields
///     are the exact same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
/// <param name="OnlineWindow">How recently a client must have been seen to be reported online.</param>
/// <param name="FlushInterval">How often buffered sightings are written to storage.</param>
/// <param name="EnableNeighborTableSignal">Whether the kernel neighbor table is sampled as a presence signal.</param>
/// <param name="NeighborTablePollInterval">How often the kernel neighbor table is sampled.</param>
public record ClientPresenceSettingsDetail(
    TimeSpan OnlineWindow,
    TimeSpan FlushInterval,
    bool EnableNeighborTableSignal,
    TimeSpan NeighborTablePollInterval
);
