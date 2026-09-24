// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This class carries a single <see cref="IDnsServerEvent" /> across a message broker: its
///     own registered <see cref="DnsServerEventTypeRegistry" /> name, alongside its JSON payload
///     (via <c>Ninlil.Common.Serializer.JsonSerializer</c>, serialized against the event's
///     own concrete type explicitly, never its static/interface type -- the exact
///     <c>JsonSerializer.Serialize(payload)</c> single-argument-overload trap already documented
///     elsewhere in this project for exactly this reason).
/// </summary>
/// <param name="EventTypeName">The published event's own registered short name.</param>
/// <param name="PayloadJson">The published event, serialized against its own concrete type.</param>
public sealed record DnsServerEventEnvelope(string EventTypeName, string PayloadJson);
