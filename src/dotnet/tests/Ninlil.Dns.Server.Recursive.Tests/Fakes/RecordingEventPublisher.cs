using Ninlil.Dns.Server.Core.Events;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class records every event published to it, in order, instead of dispatching it,
///     so tests can assert on cache eviction events.
/// </summary>
public sealed class RecordingEventPublisher : IDnsServerEventPublisher
{
    public List<IDnsServerEvent> Published { get; } = [];

    public void Publish(IDnsServerEvent domainEvent) => Published.Add(domainEvent);
}
