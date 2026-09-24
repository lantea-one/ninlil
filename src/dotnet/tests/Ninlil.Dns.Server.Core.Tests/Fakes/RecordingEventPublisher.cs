using Ninlil.Dns.Server.Core.Events;

namespace Ninlil.Dns.Server.Core.Tests.Fakes;

/// <summary>
///     This class captures every event published to it, in order, instead of dispatching it
///     anywhere -- enough to assert that a cache reported an eviction.
/// </summary>
public sealed class RecordingEventPublisher : IDnsServerEventPublisher
{
    public List<IDnsServerEvent> Published { get; } = [];

    public void Publish(IDnsServerEvent domainEvent)
    {
        lock (Published)
            Published.Add(domainEvent);
    }
}
