using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class records every published event, in order, instead of dispatching it. Other
///     test projects have the same small fake; it is copied rather than shared because it is
///     trivial.
/// </summary>
public sealed class RecordingEventPublisher : IDnsServerEventPublisher
{
    public List<IDnsServerEvent> Published { get; } = [];

    public void Publish(IDnsServerEvent domainEvent) => Published.Add(domainEvent);
}
