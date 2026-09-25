// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This record carries one <see cref="IDnsServerEvent" /> across a message broker: its
///     <see cref="DnsServerEventTypeRegistry" /> name and its JSON payload. The payload is
///     serialized with the event's concrete type; serializing as the interface would drop every
///     property but <see cref="IDnsServerEvent.OccurredAtUtc" />.
/// </summary>
/// <param name="EventTypeName">The event's registered short name.</param>
/// <param name="PayloadJson">The event, serialized with its concrete type.</param>
public sealed record DnsServerEventEnvelope(string EventTypeName, string PayloadJson);
