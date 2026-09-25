using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests;

/// <summary>
///     This class records every event published to it, in order, instead of dispatching it, so a
///     test can assert on what was published. The same small fake exists in other test projects.
/// </summary>
public sealed class RecordingEventPublisher : IDnsServerEventPublisher
{
    public List<IDnsServerEvent> Published { get; } = [];

    public void Publish(IDnsServerEvent domainEvent) => Published.Add(domainEvent);
}
