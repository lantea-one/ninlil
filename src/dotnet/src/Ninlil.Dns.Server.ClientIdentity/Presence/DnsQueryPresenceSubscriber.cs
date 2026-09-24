using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Presence;

/// <summary>
///     This subscriber turns every answered DNS query into a presence sighting of the address it
///     came from. It does nothing but hand the source address to the in-memory
///     <see cref="IClientPresenceRecorder" /> -- no identity resolution, no I/O -- so it is safe to
///     run synchronously for every query; resolution to a known client happens later, in bulk, at
///     flush time. Registered as a singleton.
/// </summary>
/// <param name="recorder">The recorder sightings are handed to.</param>
public sealed class DnsQueryPresenceSubscriber(IClientPresenceRecorder recorder)
    : IDnsServerEventSubscriber<DnsQueryAnsweredEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(DnsQueryAnsweredEvent domainEvent, CancellationToken stoppingToken)
    {
        recorder.RecordSourceAddress(domainEvent.RemoteAddress, domainEvent.OccurredAtUtc);
        return ValueTask.CompletedTask;
    }
}
