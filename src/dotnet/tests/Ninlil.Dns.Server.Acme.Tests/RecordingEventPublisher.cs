using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class captures every event published to it, in order, instead of actually
///     dispatching them anywhere -- the same small fake
///     <c>Ninlil.Dns.Server.Recursive.Tests.Fakes.RecordingEventPublisher</c> already
///     establishes for a different test project, reused here rather than shared across assemblies
///     since it is a trivial, self-contained type.
/// </summary>
public sealed class RecordingEventPublisher : IDnsServerEventPublisher
{
    public List<IDnsServerEvent> Published { get; } = [];

    public void Publish(IDnsServerEvent domainEvent) => Published.Add(domainEvent);
}
