// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This record carries one <see cref="IDnsServerEvent" /> across a message broker: its
///     <see cref="DnsServerEventTypeRegistry" /> name, its JSON payload and the instance that
///     published it. The payload is serialized with the event's concrete type; serializing as the
///     interface would drop every property but <see cref="IDnsServerEvent.OccurredAtUtc" />.
/// </summary>
/// <param name="EventTypeName">The event's registered short name.</param>
/// <param name="PayloadJson">The event, serialized with its concrete type.</param>
/// <param name="OriginInstanceId">
///     The <see cref="DnsServerEventPublisher.InstanceId" /> of the process that published the
///     event. An envelope from an older build has none (<see cref="Guid.Empty" />) and counts as
///     remote.
/// </param>
public sealed record DnsServerEventEnvelope(
    string EventTypeName,
    string PayloadJson,
    Guid OriginInstanceId = default
);
